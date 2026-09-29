using HealthcareClaimsQueue.API.Dtos;
using HealthcareClaimsQueue.API.Services;
using Microsoft.AspNetCore.Mvc;

namespace HealthcareClaimsQueue.API.Controllers;

[ApiController]
[Route("api/tasks")]
public class TaskController : ControllerBase
{
    private readonly ITaskService _taskService;
    private readonly ILogger<TaskController> _logger;
    private readonly IConfiguration _config;

    public TaskController(ITaskService taskService, ILogger<TaskController> logger, IConfiguration config)
    {
        _taskService = taskService;
        _logger = logger;
        _config = config;
    }

    /// <summary>
    /// Acquire a lock on a review task for the calling reviewer.
    /// This is an atomic operation. If two reviewers call simultaneously, exactly one succeeds (200),
    /// and the other gets 409 Conflict.
    /// Lock expires after configurable interval (default 15 minutes).
    /// </summary>
    [HttpPost("{taskId}/review")]
    [ProducesResponseType(typeof(ReviewClaimResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ReviewClaim(int taskId)
    {
        try
        {
            // Extract user ID from header
            var userId = HttpContext.Request.Headers["X-User-Id"].ToString();
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(new { error = "X-User-Id header is required." });

            // Get lock duration from config
            var lockDuration = _config.GetValue<int>("ReviewLockDurationMinutes", 15);

            var result = await _taskService.ReviewClaimAsync(taskId, userId, lockDuration);
            return Ok(result);
        }
        catch (LockConflictException lockEx)
        {
            // 409 Conflict: Another reviewer holds the lock
            _logger.LogInformation("Lock conflict on task {TaskId}: {Message}", taskId, lockEx.Message);
            return Conflict(new
            {
                error = lockEx.Message,
                lockedBy = lockEx.LockedBy,
                lockedUntil = lockEx.LockedUntil
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error acquiring lock on task {TaskId}", taskId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = "An error occurred while acquiring the lock." });
        }
    }

    /// <summary>
    /// Release the lock held by the calling reviewer.
    /// Only the reviewer who holds the lock can release it.
    /// </summary>
    [HttpPost("{taskId}/release")]
    [ProducesResponseType(typeof(ReleaseClaimResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ReleaseClaim(int taskId)
    {
        try
        {
            var userId = HttpContext.Request.Headers["X-User-Id"].ToString();
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(new { error = "X-User-Id header is required." });

            await _taskService.ReleaseClaimAsync(taskId, userId);

            return Ok(new
            {
                taskId,
                message = "Task released. Available to other reviewers."
            });
        }
        catch (UnauthorizedAccessException)
        {
            // 403 Forbidden: User doesn't hold the lock
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error releasing lock on task {TaskId}", taskId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = "An error occurred while releasing the lock." });
        }
    }

    /// <summary>
    /// Forward the task to another reviewer.
    /// This sets the task's AssignedTo and AssignedBy fields.
    /// Note: The lock is NOT released when forwarding.
    /// </summary>
    [HttpPost("{taskId}/forward")]
    [ProducesResponseType(typeof(ForwardClaimResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ForwardClaim(int taskId, [FromBody] ForwardClaimRequest request)
    {
        try
        {
            var userId = HttpContext.Request.Headers["X-User-Id"].ToString();
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(new { error = "X-User-Id header is required." });

            if (string.IsNullOrEmpty(request.TargetUserId))
                return BadRequest(new { error = "TargetUserId is required." });

            await _taskService.ForwardClaimAsync(taskId, userId, request.TargetUserId);

            return Ok(new
            {
                taskId,
                message = "Task forwarded successfully. Lock status unchanged."
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error forwarding task {TaskId}", taskId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = "An error occurred while forwarding the task." });
        }
    }

    /// <summary>
    /// Complete the review with an outcome.
    /// Outcomes: Approve, PartialDenial, Deny, Pend
    /// Deny outcome cascades to close all sibling tasks.
    /// Pend outcome creates a new task in the Pend queue.
    /// </summary>
    [HttpPost("{taskId}/complete")]
    [ProducesResponseType(typeof(CompleteReviewResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> CompleteReview(int taskId, [FromBody] CompleteReviewRequest request)
    {
        try
        {
            var userId = HttpContext.Request.Headers["X-User-Id"].ToString();
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(new { error = "X-User-Id header is required." });

            if (string.IsNullOrEmpty(request.Outcome))
                return BadRequest(new { error = "Outcome is required." });

            await _taskService.CompleteReviewAsync(taskId, userId, request.Outcome, request.Note);

            return Ok(new
            {
                taskId,
                message = $"Task completed with outcome: {request.Outcome}"
            });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error completing review on task {TaskId}", taskId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = "An error occurred while completing the review." });
        }
    }
}
