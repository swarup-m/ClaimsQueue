using HealthcareClaimsQueue.API.Data;
using HealthcareClaimsQueue.API.Models;
using HealthcareClaimsQueue.API.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Xunit;

namespace HealthcareClaimsQueue.Tests;

public class CompleteReviewTests
{
    private QueueDbContext CreateInMemoryContext()
    {
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
        // Add queues
        var queues = new List<Queue>
        {
            new Queue { QueueId = 1, QueueCode = "DUP", QueueName = "Duplicate Check" },
            new Queue { QueueId = 2, QueueCode = "AUTH", QueueName = "Authorization Matching" },
            new Queue { QueueId = 3, QueueCode = "CASE", QueueName = "Case Rate" },
            new Queue { QueueId = 5, QueueCode = "PEND", QueueName = "Pend" }
        };
        db.Queues.AddRange(queues);

        // Add users
        var users = new List<AppUser>
        {
            new AppUser { UserId = 1, Username = "ereyes", DisplayName = "Elena Reyes", Role = "Reviewer" },
            new AppUser { UserId = 2, Username = "jchen", DisplayName = "Jason Chen", Role = "Reviewer" }
        };
        db.AppUsers.AddRange(users);

        // Add claim
        var claim = new Claim { ClaimId = 1, ClaimNumber = "0000000001", MemberId = "M001", ProviderId = "P001", BilledAmount = 100, ServiceFrom = DateTime.UtcNow, ServiceTo = DateTime.UtcNow, ReceivedOn = DateTime.UtcNow };
        db.Claims.Add(claim);

        db.SaveChanges();
    }

    [Fact]
    public async Task CompleteReview_WithApproveOutcome_ClosesTask()
    {
        var db = CreateInMemoryContext();
        SeedTestData(db);

        var now = DateTime.UtcNow;

        // Create and lock a task
        var task = new ReviewTask
        {
            TaskId = 1,
            ClaimId = 1,
            QueueId = 1,
            Priority = 1,
            DueDate = now.AddHours(24),
            Status = "Open",
            CreatedAt = now,
            LockedByUserId = 1,
            LockedOn = now,
            LockExpiresOn = now.AddMinutes(15)
        };
        db.ReviewTasks.Add(task);
        db.SaveChanges();

        var config = CreateConfiguration();
        var service = new TaskService(db, new MockLogger<TaskService>(), config);

        // Complete with Approve
        await service.CompleteReviewAsync(taskId: 1, userId: "ereyes", outcome: "Approve", note: null);

        // Verify task is closed
        var completedTask = await db.ReviewTasks.FirstAsync(t => t.TaskId == 1);
        Assert.Equal("Closed", completedTask.Status);
        Assert.Equal("Approve", completedTask.Outcome);
        Assert.Equal(1, completedTask.ClosedByUserId);
        Assert.NotNull(completedTask.ClosedAt);
        Assert.Null(completedTask.LockedByUserId);
    }

    [Fact]
    public async Task CompleteReview_WithDenyOutcome_CasclosesAllSiblings()
    {
        // SCENARIO: Claim is in 3 queues (DUP, AUTH, CASE)
        // Reviewer completes DUP task with Deny
        // EXPECTED: All 3 tasks closed, but only DUP has CompletedBy set
        // (AUTH and CASE have NULL CompletedBy for cascade attribution)

        var db = CreateInMemoryContext();
        SeedTestData(db);

        var now = DateTime.UtcNow;

        // Create 3 tasks for same claim in different queues
        var dupTask = new ReviewTask { TaskId = 1, ClaimId = 1, QueueId = 1, Priority = 1, DueDate = now.AddHours(24), Status = "Open", CreatedAt = now, LockedByUserId = 1, LockedOn = now, LockExpiresOn = now.AddMinutes(15) };
        var authTask = new ReviewTask { TaskId = 2, ClaimId = 1, QueueId = 2, Priority = 2, DueDate = now.AddHours(24), Status = "Open", CreatedAt = now };
        var caseTask = new ReviewTask { TaskId = 3, ClaimId = 1, QueueId = 3, Priority = 1, DueDate = now.AddHours(24), Status = "Open", CreatedAt = now };

        db.ReviewTasks.AddRange(dupTask, authTask, caseTask);
        db.SaveChanges();

        var config = CreateConfiguration();
        var service = new TaskService(db, new MockLogger<TaskService>(), config);

        // Complete DUP task with Deny
        await service.CompleteReviewAsync(taskId: 1, userId: "ereyes", outcome: "Deny", note: "Duplicate of another claim");

        // Verify all 3 are closed
        var dupCompleted = await db.ReviewTasks.FirstAsync(t => t.TaskId == 1);
        var authCompleted = await db.ReviewTasks.FirstAsync(t => t.TaskId == 2);
        var caseCompleted = await db.ReviewTasks.FirstAsync(t => t.TaskId == 3);

        Assert.Equal("Closed", dupCompleted.Status);
        Assert.Equal("Deny", dupCompleted.Outcome);
        Assert.Equal(1, dupCompleted.ClosedByUserId); // ereyes = user_id 1

        Assert.Equal("Closed", authCompleted.Status);
        Assert.Equal("Deny", authCompleted.Outcome);
        Assert.Null(authCompleted.ClosedByUserId); // ← CASCADE: NULL (not credited)

        Assert.Equal("Closed", caseCompleted.Status);
        Assert.Equal("Deny", caseCompleted.Outcome);
        Assert.Null(caseCompleted.ClosedByUserId); // ← CASCADE: NULL (not credited)
    }

    [Fact]
    public async Task CompleteReview_WithPendOutcome_CreatesNewPendTask()
    {
        var db = CreateInMemoryContext();
        SeedTestData(db);

        var now = DateTime.UtcNow;

        var task = new ReviewTask
        {
            TaskId = 1,
            ClaimId = 1,
            QueueId = 1,
            Priority = 1,
            DueDate = now.AddHours(24),
            Status = "Open",
            CreatedAt = now,
            LockedByUserId = 1,
            LockedOn = now,
            LockExpiresOn = now.AddMinutes(15)
        };
        db.ReviewTasks.Add(task);
        db.SaveChanges();

        var config = CreateConfiguration();
        var service = new TaskService(db, new MockLogger<TaskService>(), config);

        // Complete with Pend
        await service.CompleteReviewAsync(taskId: 1, userId: "ereyes", outcome: "Pend", note: "Awaiting additional documentation");

        // Verify original task is closed
        var originalTask = await db.ReviewTasks.FirstAsync(t => t.TaskId == 1);
        Assert.Equal("Closed", originalTask.Status);
        Assert.Equal("Pend", originalTask.Outcome);
        Assert.Equal(1, originalTask.ClosedByUserId);
        Assert.NotNull(originalTask.PendedAt);

        // Verify new pend task was created
        var pendTasks = await db.ReviewTasks.Where(t => t.QueueId == 5).ToListAsync();
        Assert.Single(pendTasks);

        var pendTask = pendTasks.First();
        Assert.Equal("Open", pendTask.Status);
        Assert.Equal(1, pendTask.ClaimId);
        Assert.Equal(5, pendTask.QueueId); // Pend queue
        Assert.Equal(1, pendTask.Priority); // Default priority

        // Verify due date is 7 days from now
        var expectedDueDate = now.AddDays(7);
        Assert.True(Math.Abs((pendTask.DueDate - expectedDueDate).TotalSeconds) < 1);
    }

    [Fact]
    public async Task CompleteReview_FailsIfNotLockHolder()
    {
        var db = CreateInMemoryContext();
        SeedTestData(db);

        var now = DateTime.UtcNow;

        var task = new ReviewTask
        {
            TaskId = 1,
            ClaimId = 1,
            QueueId = 1,
            Priority = 1,
            DueDate = now.AddHours(24),
            Status = "Open",
            CreatedAt = now,
            LockedByUserId = 1, // Locked by ereyes
            LockedOn = now,
            LockExpiresOn = now.AddMinutes(15)
        };
        db.ReviewTasks.Add(task);
        db.SaveChanges();

        var config = CreateConfiguration();
        var service = new TaskService(db, new MockLogger<TaskService>(), config);

        // jchen tries to complete ereyes' task
        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => service.CompleteReviewAsync(taskId: 1, userId: "jchen", outcome: "Approve", note: null)
        );

        Assert.Contains("do not hold the lock", ex.Message);
    }

    [Fact]
    public async Task CompleteReview_FailsWithInvalidOutcome()
    {
        var db = CreateInMemoryContext();
        SeedTestData(db);

        var now = DateTime.UtcNow;

        var task = new ReviewTask
        {
            TaskId = 1,
            ClaimId = 1,
            QueueId = 1,
            Priority = 1,
            DueDate = now.AddHours(24),
            Status = "Open",
            CreatedAt = now,
            LockedByUserId = 1,
            LockedOn = now,
            LockExpiresOn = now.AddMinutes(15)
        };
        db.ReviewTasks.Add(task);
        db.SaveChanges();

        var config = CreateConfiguration();
        var service = new TaskService(db, new MockLogger<TaskService>(), config);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CompleteReviewAsync(taskId: 1, userId: "ereyes", outcome: "InvalidOutcome", note: null)
        );

        Assert.Contains("Invalid outcome", ex.Message);
    }

    [Fact]
    public async Task CompleteReview_WithPartialDenialOutcome_ClosesTask()
    {
        var db = CreateInMemoryContext();
        SeedTestData(db);

        var now = DateTime.UtcNow;

        var task = new ReviewTask
        {
            TaskId = 1,
            ClaimId = 1,
            QueueId = 1,
            Priority = 1,
            DueDate = now.AddHours(24),
            Status = "Open",
            CreatedAt = now,
            LockedByUserId = 1,
            LockedOn = now,
            LockExpiresOn = now.AddMinutes(15)
        };
        db.ReviewTasks.Add(task);
        db.SaveChanges();

        var config = CreateConfiguration();
        var service = new TaskService(db, new MockLogger<TaskService>(), config);

        // Complete with PartialDenial
        await service.CompleteReviewAsync(taskId: 1, userId: "ereyes", outcome: "PartialDenial", note: "Some charges denied");

        var completedTask = await db.ReviewTasks.FirstAsync(t => t.TaskId == 1);
        Assert.Equal("Closed", completedTask.Status);
        Assert.Equal("PartialDenial", completedTask.Outcome);
    }
}
