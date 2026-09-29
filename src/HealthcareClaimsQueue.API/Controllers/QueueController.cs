using HealthcareClaimsQueue.API.Dtos;
using HealthcareClaimsQueue.API.Services;
using Microsoft.AspNetCore.Mvc;

namespace HealthcareClaimsQueue.API.Controllers;

[ApiController]
[Route("api/queues")]
public class QueueController : ControllerBase
{
    private readonly IQueueService _queueService;
    private readonly ILogger<QueueController> _logger;

    public QueueController(IQueueService queueService, ILogger<QueueController> logger)
    {
        _queueService = queueService;
        _logger = logger;
    }

    /// <summary>
    /// List review tasks in a queue with pagination and optional filtering.
    /// Returns only OPEN tasks sorted by: Priority, DueDate, ClaimNumber.
    /// </summary>
    [HttpGet("{queueId}/tasks")]
    [ProducesResponseType(typeof(PagedReviewTasksResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ListReviewTasks(
        int queueId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100,
        [FromQuery] string? assignedTo = null,
        [FromQuery] string? lockedBy = null,
        [FromQuery] byte? priority = null)
    {
        try
        {
            // Validate pagination
            if (page < 1 || pageSize < 1 || pageSize > 500)
                return BadRequest(new { error = "Invalid pagination parameters. Page must be >= 1, pageSize 1-500." });

            // Validate priority if provided
            if (priority.HasValue && (priority < 1 || priority > 5))
                return BadRequest(new { error = "Invalid priority. Must be between 1 and 5." });

            var result = await _queueService.GetReviewTasksAsync(queueId, page, pageSize, assignedTo, lockedBy, priority);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing review tasks for queue {QueueId}", queueId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = "An error occurred while retrieving tasks." });
        }
    }

    /// <summary>
    /// Get the next available claim in the queue for the calling reviewer.
    /// This is a read-only preview; it does not acquire a lock.
    /// Two reviewers may receive the same task; lock race is resolved at Review.
    /// </summary>
    [HttpGet("{queueId}/next")]
    [ProducesResponseType(typeof(GetNextClaimResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetNextClaim(int queueId)
    {
        try
        {
            // Extract user ID from header
            var userId = HttpContext.Request.Headers["X-User-Id"].ToString();
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(new { error = "X-User-Id header is required." });

            var nextTask = await _queueService.GetNextClaimAsync(queueId, userId);

            if (nextTask == null)
                return NoContent(); // 204: No available tasks

            return Ok(nextTask);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting next claim for queue {QueueId}", queueId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = "An error occurred while retrieving the next claim." });
        }
    }
}
