using HealthcareClaimsQueue.API.Data;
using HealthcareClaimsQueue.API.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HealthcareClaimsQueue.Tests;

public class QueueSelectionAlgorithmTests
{
    private QueueDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<QueueDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new QueueDbContext(options);
    }

    private void SeedTestData(QueueDbContext db)
    {
        // Add queue
        var queue = new Queue { QueueId = 1, QueueCode = "DUP", QueueName = "Duplicate Check" };
        db.Queues.Add(queue);

        // Add users
        var user1 = new AppUser { UserId = 1, Username = "ereyes", DisplayName = "Elena Reyes", Role = "Reviewer" };
        var user2 = new AppUser { UserId = 2, Username = "jchen", DisplayName = "Jason Chen", Role = "Reviewer" };
        var user3 = new AppUser { UserId = 3, Username = "mkowalski", DisplayName = "Marta Kowalski", Role = "Reviewer" };
        db.AppUsers.AddRange(user1, user2, user3);

        // Add claims
        var claim1 = new Claim { ClaimId = 1, ClaimNumber = "0000000001", MemberId = "M001", ProviderId = "P001", BilledAmount = 100, ServiceFrom = DateTime.UtcNow, ServiceTo = DateTime.UtcNow, ReceivedOn = DateTime.UtcNow };
        var claim2 = new Claim { ClaimId = 2, ClaimNumber = "0000000002", MemberId = "M002", ProviderId = "P002", BilledAmount = 200, ServiceFrom = DateTime.UtcNow, ServiceTo = DateTime.UtcNow, ReceivedOn = DateTime.UtcNow };
        var claim3 = new Claim { ClaimId = 3, ClaimNumber = "0000000003", MemberId = "M003", ProviderId = "P003", BilledAmount = 300, ServiceFrom = DateTime.UtcNow, ServiceTo = DateTime.UtcNow, ReceivedOn = DateTime.UtcNow };
        var claim4 = new Claim { ClaimId = 4, ClaimNumber = "0000000004", MemberId = "M004", ProviderId = "P004", BilledAmount = 400, ServiceFrom = DateTime.UtcNow, ServiceTo = DateTime.UtcNow, ReceivedOn = DateTime.UtcNow };
        db.Claims.AddRange(claim1, claim2, claim3, claim4);

        db.SaveChanges();
    }

    [Fact]
    public async Task GetNextClaim_ReturnsTiedTasksOrderedByClaimNumber()
    {
        // Scenario A: Two tasks with identical priority and due date must break on claim_number
        var db = CreateInMemoryContext();
        SeedTestData(db);

        var now = DateTime.UtcNow;
        var dueDate = now.AddHours(24);

        // Create two tasks with same priority and due date
        // Task 1: Claim 0000000003
        // Task 2: Claim 0000000001 (lower claim number should sort first)
        db.ReviewTasks.AddRange(
            new ReviewTask { TaskId = 1, ClaimId = 3, QueueId = 1, Priority = 3, DueDate = dueDate, Status = "Open", CreatedAt = now },
            new ReviewTask { TaskId = 2, ClaimId = 1, QueueId = 1, Priority = 3, DueDate = dueDate, Status = "Open", CreatedAt = now }
        );
        db.SaveChanges();

        var service = new HealthcareClaimsQueue.API.Services.QueueService(db);

        // Get next claim for unassigned reviewer
        var next = await service.GetNextClaimAsync(queueId: 1, userId: "ereyes");

        // Must return claim 1 (lower claim number) which is Task 2
        Assert.NotNull(next);
        Assert.Equal("0000000001", next.ClaimNumber);
        Assert.Equal(2, next.TaskId);
    }

    [Fact]
    public async Task GetNextClaim_PrioritizesAssignedTasks()
    {
        // Scenario B: Assigned tasks take precedence over priority
        var db = CreateInMemoryContext();
        SeedTestData(db);

        var now = DateTime.UtcNow;

        // Task 1: Assigned to ereyes, Priority 5 (lowest)
        // Task 2: Unassigned, Priority 1 (highest)
        // Result: Task 1 should be returned because it's assigned
        db.ReviewTasks.AddRange(
            new ReviewTask { TaskId = 1, ClaimId = 1, QueueId = 1, Priority = 5, DueDate = now.AddHours(48), Status = "Open", CreatedAt = now, AssignedToUserId = 1 },
            new ReviewTask { TaskId = 2, ClaimId = 2, QueueId = 1, Priority = 1, DueDate = now.AddHours(24), Status = "Open", CreatedAt = now }
        );
        db.SaveChanges();

        var service = new HealthcareClaimsQueue.API.Services.QueueService(db);

        var next = await service.GetNextClaimAsync(queueId: 1, userId: "ereyes");

        Assert.NotNull(next);
        Assert.Equal("0000000001", next.ClaimNumber);
        Assert.Equal(1, next.TaskId);
    }

    [Fact]
    public async Task GetNextClaim_SkipsLiveLockedTasks()
    {
        // Scenario D: Tasks locked by others (with active lock) must be skipped
        var db = CreateInMemoryContext();
        SeedTestData(db);

        var now = DateTime.UtcNow;

        // Task 1: Locked by mkowalski, lock expires in 10 minutes (LIVE)
        // Task 2: Unassigned, no lock
        // Result: Task 2 should be returned (Task 1 is locked)
        db.ReviewTasks.AddRange(
            new ReviewTask { TaskId = 1, ClaimId = 1, QueueId = 1, Priority = 1, DueDate = now.AddHours(24), Status = "Open", CreatedAt = now, LockedByUserId = 3, LockedOn = now.AddMinutes(-5), LockExpiresOn = now.AddMinutes(10) },
            new ReviewTask { TaskId = 2, ClaimId = 2, QueueId = 1, Priority = 2, DueDate = now.AddHours(24), Status = "Open", CreatedAt = now }
        );
        db.SaveChanges();

        var service = new HealthcareClaimsQueue.API.Services.QueueService(db);

        var next = await service.GetNextClaimAsync(queueId: 1, userId: "ereyes");

        Assert.NotNull(next);
        Assert.Equal("0000000002", next.ClaimNumber);
        Assert.Equal(2, next.TaskId);
    }

    [Fact]
    public async Task GetNextClaim_SelectsExpiredLockedTasks()
    {
        // Scenario C: Tasks with expired locks should be selectable again
        var db = CreateInMemoryContext();
        SeedTestData(db);

        var now = DateTime.UtcNow;

        // Task 1: Locked by mkowalski, lock expired 25 minutes ago (EXPIRED)
        // Task 2: Unassigned, no lock
        // Result: Task 1 should be returned (highest priority, lock expired)
        db.ReviewTasks.AddRange(
            new ReviewTask { TaskId = 1, ClaimId = 1, QueueId = 1, Priority = 1, DueDate = now.AddHours(8), Status = "Open", CreatedAt = now, LockedByUserId = 3, LockedOn = now.AddMinutes(-40), LockExpiresOn = now.AddMinutes(-25) },
            new ReviewTask { TaskId = 2, ClaimId = 2, QueueId = 1, Priority = 2, DueDate = now.AddHours(24), Status = "Open", CreatedAt = now }
        );
        db.SaveChanges();

        var service = new HealthcareClaimsQueue.API.Services.QueueService(db);

        var next = await service.GetNextClaimAsync(queueId: 1, userId: "ereyes");

        Assert.NotNull(next);
        Assert.Equal("0000000001", next.ClaimNumber);
        Assert.Equal(1, next.TaskId);
    }

    [Fact]
    public async Task GetNextClaim_ReturnsNullWhenNoTasksAvailable()
    {
        var db = CreateInMemoryContext();
        SeedTestData(db);

        var service = new HealthcareClaimsQueue.API.Services.QueueService(db);

        var next = await service.GetNextClaimAsync(queueId: 1, userId: "ereyes");

        Assert.Null(next);
    }
}
