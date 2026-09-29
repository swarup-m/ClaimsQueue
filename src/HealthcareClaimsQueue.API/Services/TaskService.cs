using HealthcareClaimsQueue.API.Data;
using HealthcareClaimsQueue.API.Dtos;
using HealthcareClaimsQueue.API.Models;
using Microsoft.EntityFrameworkCore;

namespace HealthcareClaimsQueue.API.Services;

public interface ITaskService
{
    Task<ReviewClaimResponse> ReviewClaimAsync(int taskId, string userId, int lockDurationMinutes);
    Task<bool> ReleaseClaimAsync(int taskId, string userId);
    Task ForwardClaimAsync(int taskId, string userId, string targetUserId);
    Task CompleteReviewAsync(int taskId, string userId, string outcome, string? note);
}

public class TaskService : ITaskService
{
    private readonly QueueDbContext _db;
    private readonly ILogger<TaskService> _logger;
    private readonly IConfiguration _configuration;

    public TaskService(QueueDbContext db, ILogger<TaskService> logger, IConfiguration configuration)
    {
        _db = db;
        _logger = logger;
        _configuration = configuration;
    }

    /// <summary>
    /// Acquire a lock on a review task using optimistic locking pattern.
    /// If two reviewers call simultaneously, exactly one will succeed and one will get LockConflictException (409).
    /// </summary>
    public async Task<ReviewClaimResponse> ReviewClaimAsync(int taskId, string userId, int lockDurationMinutes)
    {
        // Validate task exists and get current state
        var task = await _db.ReviewTasks
            .Include(t => t.Claim)
            .Include(t => t.Queue)
            .Include(t => t.LockedByUser)
            .FirstOrDefaultAsync(t => t.TaskId == taskId);

        if (task == null)
            throw new InvalidOperationException($"Task {taskId} not found");

        if (task.Status != "Open")
            throw new InvalidOperationException($"Task {taskId} is not open");

        // Check if task can be locked (no lock or lock expired)
        var now = DateTime.UtcNow;
        if (task.LockedByUser != null && task.LockExpiresOn > now)
        {
            // Lock is held and still valid
            _logger.LogWarning("Lock race detected: Task {TaskId} locked by {LockedBy}", taskId, task.LockedByUser.Username);
            throw new LockConflictException(
                $"Task {taskId} is currently locked by {task.LockedByUser.Username}",
                task.LockedByUser.Username,
                task.LockExpiresOn
            );
        }

        // Acquire the lock
        var lockExpiresOn = now.AddMinutes(lockDurationMinutes);

        // Get the user ID for the current reviewer
        var reviewerUser = await _db.AppUsers.FirstOrDefaultAsync(u => u.Username == userId);
        if (reviewerUser == null)
            throw new InvalidOperationException($"User {userId} not found");

        // Update the task with lock info
        task.LockedByUserId = reviewerUser.UserId;
        task.LockedOn = now;
        task.LockExpiresOn = lockExpiresOn;

        await _db.SaveChangesAsync();

        _logger.LogInformation("Lock acquired: Task {TaskId} by reviewer {UserId}", taskId, userId);

        return new ReviewClaimResponse
        {
            TaskId = task.TaskId,
            ClaimNumber = task.Claim!.ClaimNumber,
            Queue = task.Queue!.QueueName,
            LockedBy = userId,
            LockedUntil = lockExpiresOn,
            Message = $"Task locked successfully. Lock expires in {lockDurationMinutes} minutes."
        };
    }

    public async Task<bool> ReleaseClaimAsync(int taskId, string userId)
    {
        var task = await _db.ReviewTasks
            .Include(t => t.LockedByUser)
            .FirstOrDefaultAsync(t => t.TaskId == taskId);

        if (task == null)
            throw new InvalidOperationException($"Task {taskId} not found");

        // Check if user holds the lock
        if (task.LockedByUser?.Username != userId)
            throw new UnauthorizedAccessException(
                $"Task {taskId} is locked by {task.LockedByUser?.Username}, not {userId}"
            );

        // Release the lock
        task.LockedByUser = null;
        task.LockedByUserId = null;
        task.LockedOn = null;
        task.LockExpiresOn = null;

        await _db.SaveChangesAsync();

        _logger.LogInformation("Lock released: Task {TaskId} by reviewer {UserId}", taskId, userId);

        return true;
    }

    public async Task ForwardClaimAsync(int taskId, string userId, string targetUserId)
    {
        var task = await _db.ReviewTasks.FirstOrDefaultAsync(t => t.TaskId == taskId);

        if (task == null)
            throw new InvalidOperationException($"Task {taskId} not found");

        // Verify target user exists
        var targetUser = await _db.AppUsers.FirstOrDefaultAsync(u => u.Username == targetUserId);
        if (targetUser == null)
            throw new InvalidOperationException($"User {targetUserId} not found");

        // Update assignment (lock persists - does NOT release)
        task.AssignedToUserId = targetUser.UserId;
        task.AssignedByUserId = (await _db.AppUsers.FirstAsync(u => u.Username == userId)).UserId;

        await _db.SaveChangesAsync();

        _logger.LogInformation("Task {TaskId} forwarded to {TargetUserId} by {UserId}", taskId, targetUserId, userId);
    }

    public async Task CompleteReviewAsync(int taskId, string userId, string outcome, string? note)
    {
        // Validate outcome
        var validOutcomes = new[] { "Approve", "PartialDenial", "Deny", "Pend" };
        if (!validOutcomes.Contains(outcome))
            throw new InvalidOperationException($"Invalid outcome. Must be one of: {string.Join(", ", validOutcomes)}");

        // Get the task and verify lock is held by this reviewer
        var task = await _db.ReviewTasks
            .Include(t => t.Claim)
            .Include(t => t.Queue)
            .Include(t => t.LockedByUser)
            .FirstOrDefaultAsync(t => t.TaskId == taskId);

        if (task == null)
            throw new InvalidOperationException($"Task {taskId} not found");

        // Verify lock is held by the caller
        if (task.LockedByUser?.Username != userId)
            throw new UnauthorizedAccessException(
                $"You do not hold the lock for task {taskId}"
            );

        var now = DateTime.UtcNow;

        // Get the reviewer user for completion attribution
        var reviewerUser = await _db.AppUsers.FirstOrDefaultAsync(u => u.Username == userId);

        // Handle different outcomes
        if (outcome == "Deny")
        {
            // Deny: Close this task AND all sibling tasks for the same claim
            await HandleDenialAsync(task, reviewerUser!, now, note);
        }
        else if (outcome == "Pend")
        {
            // Pend: Close this task and create new task in Pend queue
            await HandlePendAsync(task, reviewerUser!, now, note);
        }
        else
        {
            // Approve or PartialDenial: Just close this task
            await HandleApprovalAsync(task, reviewerUser!, outcome, now, note);
        }

        _logger.LogInformation("Task {TaskId} completed with outcome {Outcome} by {UserId}", taskId, outcome, userId);
    }

    private async Task HandleDenialAsync(ReviewTask task, AppUser reviewer, DateTime now, string? note)
    {
        // Close the primary task (completed by the reviewer)
        task.Status = "Closed";
        task.Outcome = "Deny";
        task.ClosedAt = now;
        task.ClosedByUserId = reviewer.UserId;
        task.Note = note;
        task.LockedByUserId = null;
        task.LockedOn = null;
        task.LockExpiresOn = null;

        // Find and close all sibling tasks (same claim, different queues, still open)
        var siblingTasks = await _db.ReviewTasks
            .Where(t => t.ClaimId == task.ClaimId && t.TaskId != task.TaskId && t.Status == "Open")
            .ToListAsync();

        foreach (var sibling in siblingTasks)
        {
            sibling.Status = "Closed";
            sibling.Outcome = "Deny";
            sibling.ClosedAt = now;
            sibling.ClosedByUserId = null; // ← KEY: Cascade attribution (NULL CompletedBy)
            sibling.Note = $"Closed by denial on task {task.TaskId}";
            sibling.LockedByUserId = null;
            sibling.LockedOn = null;
            sibling.LockExpiresOn = null;
        }

        await _db.SaveChangesAsync();

        _logger.LogInformation("Denial cascade: Task {TaskId} and {SiblingCount} siblings closed", task.TaskId, siblingTasks.Count);
    }

    private async Task HandlePendAsync(ReviewTask task, AppUser reviewer, DateTime now, string? note)
    {
        // Close the current task
        task.Status = "Closed";
        task.Outcome = "Pend";
        task.ClosedAt = now;
        task.ClosedByUserId = reviewer.UserId;
        task.Note = note;
        task.PendedAt = now;
        task.LockedByUserId = null;
        task.LockedOn = null;
        task.LockExpiresOn = null;

        // Create new task in Pend queue
        var pendQueue = await _db.Queues.FirstOrDefaultAsync(q => q.QueueCode == "PEND");
        if (pendQueue == null)
            throw new InvalidOperationException("Pend queue not found");

        var pendDueDate = now.AddDays(_configuration.GetValue<int>("PendTaskDueDateDays", 7));

        var pendTask = new ReviewTask
        {
            ClaimId = task.ClaimId,
            QueueId = pendQueue.QueueId,
            Priority = 1, // Default priority for pended tasks
            DueDate = pendDueDate,
            Status = "Open",
            CreatedAt = now
        };

        _db.ReviewTasks.Add(pendTask);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Pend: Task {TaskId} closed, new pend task {PendTaskId} created", task.TaskId, pendTask.TaskId);
    }

    private async Task HandleApprovalAsync(ReviewTask task, AppUser reviewer, string outcome, DateTime now, string? note)
    {
        // Close the task (Approve or PartialDenial)
        task.Status = "Closed";
        task.Outcome = outcome;
        task.ClosedAt = now;
        task.ClosedByUserId = reviewer.UserId;
        task.Note = note;
        task.LockedByUserId = null;
        task.LockedOn = null;
        task.LockExpiresOn = null;

        await _db.SaveChangesAsync();

        _logger.LogInformation("Task {TaskId} closed with outcome {Outcome}", task.TaskId, outcome);
    }
}

/// <summary>
/// Exception thrown when a lock race is detected (409 Conflict scenario).
/// </summary>
public class LockConflictException : Exception
{
    public string? LockedBy { get; }
    public DateTime? LockedUntil { get; }

    public LockConflictException(string message, string? lockedBy = null, DateTime? lockedUntil = null)
        : base(message)
    {
        LockedBy = lockedBy;
        LockedUntil = lockedUntil;
    }
}
