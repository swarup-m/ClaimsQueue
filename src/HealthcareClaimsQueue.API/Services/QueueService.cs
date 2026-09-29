using HealthcareClaimsQueue.API.Data;
using HealthcareClaimsQueue.API.Dtos;
using HealthcareClaimsQueue.API.Models;
using Microsoft.EntityFrameworkCore;

namespace HealthcareClaimsQueue.API.Services;

public interface IQueueService
{
    Task<PagedReviewTasksResponse> GetReviewTasksAsync(int queueId, int page = 1, int pageSize = 100,
        string? assignedTo = null, string? lockedBy = null, byte? priority = null);

    Task<GetNextClaimResponse?> GetNextClaimAsync(int queueId, string userId);
}

public class QueueService : IQueueService
{
    private readonly QueueDbContext _db;

    public QueueService(QueueDbContext db)
    {
        _db = db;
    }

    public async Task<PagedReviewTasksResponse> GetReviewTasksAsync(int queueId, int page = 1, int pageSize = 100,
        string? assignedTo = null, string? lockedBy = null, byte? priority = null)
    {
        // Validate queue exists
        var queueExists = await _db.Queues.AnyAsync(q => q.QueueId == queueId);
        if (!queueExists)
            throw new ArgumentException($"Queue {queueId} not found");

        // Build base query for open tasks in this queue with includes
        IQueryable<ReviewTask> query = _db.ReviewTasks
            .Where(t => t.QueueId == queueId && t.Status == "Open")
            .Include(t => t.Claim)
            .Include(t => t.Queue)
            .Include(t => t.AssignedToUser)
            .Include(t => t.LockedByUser);

        // Apply filters
        if (!string.IsNullOrEmpty(assignedTo))
            query = query.Where(t => t.AssignedToUser != null && t.AssignedToUser.Username == assignedTo);

        if (!string.IsNullOrEmpty(lockedBy))
            query = query.Where(t => t.LockedByUser != null && t.LockedByUser.Username == lockedBy);

        if (priority.HasValue)
            query = query.Where(t => t.Priority == priority.Value);

        // Get total count
        var totalCount = await query.CountAsync();

        // Sort and paginate
        // Order: Priority ASC (lower = higher), DueDate ASC, ClaimNumber ASC
        var tasks = await query
            .OrderBy(t => t.Priority)
            .ThenBy(t => t.DueDate)
            .ThenBy(t => t.Claim!.ClaimNumber)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        // Map to DTOs
        var dtos = tasks.Select(MapToDto).ToList();

        // Calculate page info
        var totalPages = (totalCount + pageSize - 1) / pageSize;

        return new PagedReviewTasksResponse
        {
            Data = dtos,
            PageInfo = new PageInfo
            {
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize,
                TotalPages = totalPages
            }
        };
    }

    public async Task<GetNextClaimResponse?> GetNextClaimAsync(int queueId, string userId)
    {
        // Validate queue exists
        var queueExists = await _db.Queues.AnyAsync(q => q.QueueId == queueId);
        if (!queueExists)
            throw new ArgumentException($"Queue {queueId} not found");

        var now = DateTime.UtcNow;

        // Selection algorithm:
        // 1. Assigned to current user
        // 2. Higher priority (lower priority number)
        // 3. Earliest due date
        // 4. Lowest claim number
        // Exclude: Locked by others (unless lock expired)

        var task = await _db.ReviewTasks
            .Where(t => t.QueueId == queueId && t.Status == "Open")
            .Where(t => t.LockedByUser == null || t.LockExpiresOn < now) // Not locked or lock expired
            .Include(t => t.Claim)
            .Include(t => t.Queue)
            .OrderBy(t => t.AssignedToUser != null && t.AssignedToUser.Username == userId ? 0 : 1) // Assigned first
            .ThenBy(t => t.Priority)      // Higher priority (lower number)
            .ThenBy(t => t.DueDate)       // Earlier due date
            .ThenBy(t => t.Claim!.ClaimNumber) // Lowest claim number
            .FirstOrDefaultAsync();

        if (task == null)
            return null;

        return new GetNextClaimResponse
        {
            TaskId = task.TaskId,
            ClaimNumber = task.Claim!.ClaimNumber,
            Queue = task.Queue!.QueueName,
            Priority = task.Priority,
            DueDate = task.DueDate,
            AssignedTo = task.AssignedToUser?.Username,
            LockedBy = task.LockedByUser?.Username,
            DaysUntilDue = (int)(task.DueDate - DateTime.UtcNow).TotalDays
        };
    }

    private ReviewTaskDto MapToDto(ReviewTask task)
    {
        return new ReviewTaskDto
        {
            TaskId = task.TaskId,
            ClaimNumber = task.Claim!.ClaimNumber,
            Queue = task.Queue!.QueueName,
            Priority = task.Priority,
            DueDate = task.DueDate,
            AssignedTo = task.AssignedToUser?.Username,
            AssignedBy = task.AssignedByUser?.Username,
            AssignedDate = null, // Not tracked in current schema, can add if needed
            LockedBy = task.LockedByUser?.Username,
            LockedUntil = task.LockExpiresOn,
            Status = task.Status,
            CreatedAt = task.CreatedAt ?? DateTime.UtcNow
        };
    }
}
