using HealthcareClaimsQueue.API.Data;
using HealthcareClaimsQueue.API.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HealthcareClaimsQueue.Tests;

public class QueueListTests
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
        // Add queues
        var queue1 = new Queue { QueueId = 1, QueueCode = "DUP", QueueName = "Duplicate Check" };
        var queue2 = new Queue { QueueId = 2, QueueCode = "AUTH", QueueName = "Authorization Matching" };
        db.Queues.AddRange(queue1, queue2);

        // Add users
        var user1 = new AppUser { UserId = 1, Username = "ereyes", DisplayName = "Elena Reyes", Role = "Reviewer" };
        var user2 = new AppUser { UserId = 2, Username = "jchen", DisplayName = "Jason Chen", Role = "Reviewer" };
        db.AppUsers.AddRange(user1, user2);

        // Add claims
        for (int i = 1; i <= 5; i++)
        {
            var claim = new Claim
            {
                ClaimId = i,
                ClaimNumber = $"000000000{i}",
                MemberId = $"M00{i}",
                ProviderId = $"P00{i}",
                BilledAmount = 100 * i,
                ServiceFrom = DateTime.UtcNow,
                ServiceTo = DateTime.UtcNow,
                ReceivedOn = DateTime.UtcNow
            };
            db.Claims.Add(claim);
        }

        db.SaveChanges();
    }

    [Fact]
    public async Task GetReviewTasks_ReturnsSortedByPriorityDueDateClaimNumber()
    {
        var db = CreateInMemoryContext();
        SeedTestData(db);

        var now = DateTime.UtcNow;

        // Create 5 tasks with different priorities and due dates
        db.ReviewTasks.AddRange(
            new ReviewTask { TaskId = 1, ClaimId = 1, QueueId = 1, Priority = 2, DueDate = now.AddHours(24), Status = "Open", CreatedAt = now },
            new ReviewTask { TaskId = 2, ClaimId = 2, QueueId = 1, Priority = 1, DueDate = now.AddHours(24), Status = "Open", CreatedAt = now },
            new ReviewTask { TaskId = 3, ClaimId = 3, QueueId = 1, Priority = 1, DueDate = now.AddHours(12), Status = "Open", CreatedAt = now },
            new ReviewTask { TaskId = 4, ClaimId = 4, QueueId = 1, Priority = 3, DueDate = now.AddHours(36), Status = "Open", CreatedAt = now },
            new ReviewTask { TaskId = 5, ClaimId = 5, QueueId = 1, Priority = 1, DueDate = now.AddHours(24), Status = "Open", CreatedAt = now }
        );
        db.SaveChanges();

        var service = new HealthcareClaimsQueue.API.Services.QueueService(db);

        var result = await service.GetReviewTasksAsync(queueId: 1, page: 1, pageSize: 100);

        Assert.NotNull(result);
        Assert.Equal(5, result.Data.Count);
        Assert.Equal(5, result.PageInfo.TotalCount);

        // Verify sort order:
        // Priority 1 comes first, then by due date, then by claim number
        // Task 3: Priority 1, earliest due date (12 hours)
        // Task 2: Priority 1, later due date, claim 2
        // Task 5: Priority 1, same due date as Task 2, claim 5 (later than claim 2)
        // Task 1: Priority 2
        // Task 4: Priority 3

        Assert.Equal(3, result.Data[0].TaskId);
        Assert.Equal(2, result.Data[1].TaskId);
        Assert.Equal(5, result.Data[2].TaskId);
        Assert.Equal(1, result.Data[3].TaskId);
        Assert.Equal(4, result.Data[4].TaskId);
    }

    [Fact]
    public async Task GetReviewTasks_ReturnsOnlyOpenTasks()
    {
        var db = CreateInMemoryContext();
        SeedTestData(db);

        var now = DateTime.UtcNow;

        // Create mix of open and closed tasks
        db.ReviewTasks.AddRange(
            new ReviewTask { TaskId = 1, ClaimId = 1, QueueId = 1, Priority = 1, DueDate = now.AddHours(24), Status = "Open", CreatedAt = now },
            new ReviewTask { TaskId = 2, ClaimId = 2, QueueId = 1, Priority = 1, DueDate = now.AddHours(24), Status = "Closed", CreatedAt = now, ClosedAt = now, Outcome = "Approve" },
            new ReviewTask { TaskId = 3, ClaimId = 3, QueueId = 1, Priority = 1, DueDate = now.AddHours(24), Status = "Open", CreatedAt = now }
        );
        db.SaveChanges();

        var service = new HealthcareClaimsQueue.API.Services.QueueService(db);

        var result = await service.GetReviewTasksAsync(queueId: 1, page: 1, pageSize: 100);

        Assert.Equal(2, result.Data.Count);
        Assert.All(result.Data, t => Assert.Equal("Open", t.Status));
    }

    [Fact]
    public async Task GetReviewTasks_SupportsPagination()
    {
        var db = CreateInMemoryContext();
        SeedTestData(db);

        var now = DateTime.UtcNow;

        // Create 10 tasks
        for (int i = 1; i <= 10; i++)
        {
            db.ReviewTasks.Add(new ReviewTask
            {
                TaskId = i,
                ClaimId = (i % 5) + 1,
                QueueId = 1,
                Priority = (byte)((i % 3) + 1),
                DueDate = now.AddHours(i),
                Status = "Open",
                CreatedAt = now
            });
        }
        db.SaveChanges();

        var service = new HealthcareClaimsQueue.API.Services.QueueService(db);

        // Get page 1 with page size 3
        var page1 = await service.GetReviewTasksAsync(queueId: 1, page: 1, pageSize: 3);
        Assert.Equal(3, page1.Data.Count);
        Assert.Equal(10, page1.PageInfo.TotalCount);
        Assert.Equal(1, page1.PageInfo.Page);
        Assert.Equal(3, page1.PageInfo.PageSize);
        Assert.Equal(4, page1.PageInfo.TotalPages);

        // Get page 2
        var page2 = await service.GetReviewTasksAsync(queueId: 1, page: 2, pageSize: 3);
        Assert.Equal(3, page2.Data.Count);
        Assert.Equal(2, page2.PageInfo.Page);

        // Get page 4 (last page)
        var page4 = await service.GetReviewTasksAsync(queueId: 1, page: 4, pageSize: 3);
        Assert.Single(page4.Data);
    }

    [Fact]
    public async Task GetReviewTasks_FiltersByAssignedTo()
    {
        var db = CreateInMemoryContext();
        SeedTestData(db);

        var now = DateTime.UtcNow;

        // Create tasks: one assigned to ereyes, one to jchen, one unassigned
        db.ReviewTasks.AddRange(
            new ReviewTask { TaskId = 1, ClaimId = 1, QueueId = 1, Priority = 1, DueDate = now.AddHours(24), Status = "Open", CreatedAt = now, AssignedToUserId = 1 },
            new ReviewTask { TaskId = 2, ClaimId = 2, QueueId = 1, Priority = 1, DueDate = now.AddHours(24), Status = "Open", CreatedAt = now, AssignedToUserId = 2 },
            new ReviewTask { TaskId = 3, ClaimId = 3, QueueId = 1, Priority = 1, DueDate = now.AddHours(24), Status = "Open", CreatedAt = now }
        );
        db.SaveChanges();

        var service = new HealthcareClaimsQueue.API.Services.QueueService(db);

        // Filter for ereyes
        var result = await service.GetReviewTasksAsync(queueId: 1, page: 1, pageSize: 100, assignedTo: "ereyes");

        Assert.Single(result.Data);
        Assert.Equal(1, result.Data[0].TaskId);
        Assert.Equal("ereyes", result.Data[0].AssignedTo);
    }

    [Fact]
    public async Task GetReviewTasks_ThrowsWhenQueueNotFound()
    {
        var db = CreateInMemoryContext();
        SeedTestData(db);

        var service = new HealthcareClaimsQueue.API.Services.QueueService(db);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.GetReviewTasksAsync(queueId: 999, page: 1, pageSize: 100)
        );
    }
}
