using HealthcareClaimsQueue.API.Data;
using HealthcareClaimsQueue.API.Models;
using HealthcareClaimsQueue.API.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Xunit;

namespace HealthcareClaimsQueue.Tests;

public class LockConcurrencyTests
{
    private QueueDbContext CreateInMemoryContext()
    {
        // Use in-memory database for testing
        var options = new DbContextOptionsBuilder<QueueDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new QueueDbContext(options);
    }

    private IConfiguration CreateConfiguration()
    {
        var inMemorySettings = new Dictionary<string, string?>
        {
            { "PendTaskDueDateDays", "7" }
        };

        return new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();
    }

    private void SeedTestData(QueueDbContext db)
    {
        // Add queue
        var queue = new Queue { QueueId = 1, QueueCode = "DUP", QueueName = "Duplicate Check" };
        db.Queues.Add(queue);

        // Add users
        var user1 = new AppUser { UserId = 1, Username = "ereyes", DisplayName = "Elena Reyes", Role = "Reviewer" };
        var user2 = new AppUser { UserId = 2, Username = "jchen", DisplayName = "Jason Chen", Role = "Reviewer" };
        db.AppUsers.AddRange(user1, user2);

        // Add claim
        var claim = new Claim { ClaimId = 1, ClaimNumber = "0000000001", MemberId = "M001", ProviderId = "P001", BilledAmount = 100, ServiceFrom = DateTime.UtcNow, ServiceTo = DateTime.UtcNow, ReceivedOn = DateTime.UtcNow };
        db.Claims.Add(claim);

        db.SaveChanges();
    }

    [Fact]
    public async Task ReviewClaim_WhenTwoCallsRace_ExactlyOneSucceeds()
    {
        // This is the critical concurrency test
        // SCENARIO: Two reviewers click "Review" simultaneously on the same task
        // EXPECTED: Exactly one gets 200 OK, one gets 409 Conflict
        // This validates atomic lock acquisition

        var db = CreateInMemoryContext();
        SeedTestData(db);

        var now = DateTime.UtcNow;

        // Create one task that both reviewers will try to lock
        db.ReviewTasks.Add(new ReviewTask
        {
            TaskId = 1,
            ClaimId = 1,
            QueueId = 1,
            Priority = 1,
            DueDate = now.AddHours(24),
            Status = "Open",
            CreatedAt = now
        });
        db.SaveChanges();

        var config = CreateConfiguration();
        var service = new TaskService(db, new MockLogger<TaskService>(), config);

        // Create two concurrent tasks that both try to acquire the lock
        var reviewer1Lock = service.ReviewClaimAsync(taskId: 1, userId: "ereyes", lockDurationMinutes: 15);
        var reviewer2Lock = service.ReviewClaimAsync(taskId: 1, userId: "jchen", lockDurationMinutes: 15);

        // Wait for both to complete
        var results = await Task.WhenAll(
            reviewer1Lock.ContinueWith(t => new { Success = t.IsCompletedSuccessfully, Task = t }),
            reviewer2Lock.ContinueWith(t => new { Success = t.IsCompletedSuccessfully, Task = t })
        );

        // Exactly one should succeed, one should throw LockConflictException
        var successCount = results.Count(r => r.Success);
        var failureCount = results.Count(r => !r.Success);

        Assert.Equal(1, successCount);
        Assert.Equal(1, failureCount);

        // Verify the failed one threw LockConflictException (409)
        var failedTask = results.First(r => !r.Success).Task;
        Assert.IsType<AggregateException>(failedTask.Exception);
        Assert.IsType<LockConflictException>(failedTask.Exception!.InnerException);
    }

    [Fact]
    public async Task ReviewClaim_SucceedsWhenLockExpired()
    {
        var db = CreateInMemoryContext();
        SeedTestData(db);

        var now = DateTime.UtcNow;

        // Create a task with an expired lock
        db.ReviewTasks.Add(new ReviewTask
        {
            TaskId = 1,
            ClaimId = 1,
            QueueId = 1,
            Priority = 1,
            DueDate = now.AddHours(24),
            Status = "Open",
            CreatedAt = now,
            LockedByUserId = 1,
            LockedOn = now.AddMinutes(-20),
            LockExpiresOn = now.AddMinutes(-5) // EXPIRED
        });
        db.SaveChanges();

        var config = CreateConfiguration();
        var service = new TaskService(db, new MockLogger<TaskService>(), config);

        // Another reviewer should be able to acquire the expired lock
        var result = await service.ReviewClaimAsync(taskId: 1, userId: "jchen", lockDurationMinutes: 15);

        Assert.NotNull(result);
        Assert.Equal("jchen", result.LockedBy);
        Assert.Equal(1, result.TaskId);
    }

    [Fact]
    public async Task ReviewClaim_FailsWhenLockedLive()
    {
        var db = CreateInMemoryContext();
        SeedTestData(db);

        var now = DateTime.UtcNow;

        // Create a task with a live (non-expired) lock
        db.ReviewTasks.Add(new ReviewTask
        {
            TaskId = 1,
            ClaimId = 1,
            QueueId = 1,
            Priority = 1,
            DueDate = now.AddHours(24),
            Status = "Open",
            CreatedAt = now,
            LockedByUserId = 1,
            LockedOn = now.AddMinutes(-5),
            LockExpiresOn = now.AddMinutes(10) // LIVE
        });
        db.SaveChanges();

        var config = CreateConfiguration();
        var service = new TaskService(db, new MockLogger<TaskService>(), config);

        // Another reviewer should NOT be able to acquire a live lock
        var ex = await Assert.ThrowsAsync<LockConflictException>(
            () => service.ReviewClaimAsync(taskId: 1, userId: "jchen", lockDurationMinutes: 15)
        );

        Assert.Contains("locked by", ex.Message);
    }

    [Fact]
    public async Task ReviewClaim_UpdatesLockFields()
    {
        var db = CreateInMemoryContext();
        SeedTestData(db);

        var now = DateTime.UtcNow;

        db.ReviewTasks.Add(new ReviewTask
        {
            TaskId = 1,
            ClaimId = 1,
            QueueId = 1,
            Priority = 1,
            DueDate = now.AddHours(24),
            Status = "Open",
            CreatedAt = now
        });
        db.SaveChanges();

        var config = CreateConfiguration();
        var service = new TaskService(db, new MockLogger<TaskService>(), config);

        var lockDuration = 15;
        var result = await service.ReviewClaimAsync(taskId: 1, userId: "ereyes", lockDurationMinutes: lockDuration);

        // Verify lock fields were set correctly
        Assert.Equal("ereyes", result.LockedBy);
        Assert.True(result.LockedUntil > now);
        Assert.True(result.LockedUntil <= now.AddMinutes(lockDuration + 1)); // Allow 1 min buffer

        // Verify in database
        var task = await db.ReviewTasks.FirstAsync(t => t.TaskId == 1);
        Assert.Equal(1, task.LockedByUserId); // ereyes = user_id 1
        Assert.NotNull(task.LockedOn);
        Assert.NotNull(task.LockExpiresOn);
    }

    [Fact]
    public async Task ReleaseClaim_OnlyAllowsLockHolder()
    {
        var db = CreateInMemoryContext();
        SeedTestData(db);

        var now = DateTime.UtcNow;

        // Create a locked task
        db.ReviewTasks.Add(new ReviewTask
        {
            TaskId = 1,
            ClaimId = 1,
            QueueId = 1,
            Priority = 1,
            DueDate = now.AddHours(24),
            Status = "Open",
            CreatedAt = now,
            LockedByUserId = 1, // ereyes
            LockedOn = now,
            LockExpiresOn = now.AddMinutes(15)
        });
        db.SaveChanges();

        var config = CreateConfiguration();
        var service = new TaskService(db, new MockLogger<TaskService>(), config);

        // jchen should NOT be able to release a lock held by ereyes
        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => service.ReleaseClaimAsync(taskId: 1, userId: "jchen")
        );

        // ereyes should be able to release their own lock
        var releaseResult = await service.ReleaseClaimAsync(taskId: 1, userId: "ereyes");
        Assert.True(releaseResult);

        // Verify lock is cleared
        var task = await db.ReviewTasks.FirstAsync(t => t.TaskId == 1);
        Assert.Null(task.LockedByUserId);
        Assert.Null(task.LockedOn);
        Assert.Null(task.LockExpiresOn);
    }

    [Fact]
    public async Task ReviewClaim_ThrowsWhenTaskNotFound()
    {
        var db = CreateInMemoryContext();
        SeedTestData(db);

        var config = CreateConfiguration();
        var service = new TaskService(db, new MockLogger<TaskService>(), config);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ReviewClaimAsync(taskId: 999, userId: "ereyes", lockDurationMinutes: 15)
        );

        Assert.Contains("not found", ex.Message);
    }

    [Fact]
    public async Task ReviewClaim_ThrowsWhenTaskNotOpen()
    {
        var db = CreateInMemoryContext();
        SeedTestData(db);

        var now = DateTime.UtcNow;

        // Create a closed task
        db.ReviewTasks.Add(new ReviewTask
        {
            TaskId = 1,
            ClaimId = 1,
            QueueId = 1,
            Priority = 1,
            DueDate = now.AddHours(24),
            Status = "Closed", // CLOSED
            CreatedAt = now,
            ClosedAt = now,
            Outcome = "Approve"
        });
        db.SaveChanges();

        var config = CreateConfiguration();
        var service = new TaskService(db, new MockLogger<TaskService>(), config);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ReviewClaimAsync(taskId: 1, userId: "ereyes", lockDurationMinutes: 15)
        );

        Assert.Contains("not open", ex.Message);
    }
}

/// <summary>
/// Mock logger for testing
/// </summary>
public class MockLogger<T> : ILogger<T>
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
}
