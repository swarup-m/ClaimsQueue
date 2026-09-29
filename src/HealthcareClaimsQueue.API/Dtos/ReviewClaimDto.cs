namespace HealthcareClaimsQueue.API.Dtos;

public class ReviewClaimResponse
{
    public int TaskId { get; set; }
    public string ClaimNumber { get; set; } = null!;
    public string Queue { get; set; } = null!;
    public string LockedBy { get; set; } = null!;
    public DateTime LockedUntil { get; set; }
    public string Message { get; set; } = null!;
}

public class ReleaseClaimResponse
{
    public int TaskId { get; set; }
    public string ClaimNumber { get; set; } = null!;
    public string Message { get; set; } = null!;
}

public class ForwardClaimRequest
{
    public string TargetUserId { get; set; } = null!;
    public string? Note { get; set; }
}

public class ForwardClaimResponse
{
    public int TaskId { get; set; }
    public string ClaimNumber { get; set; } = null!;
    public string AssignedTo { get; set; } = null!;
    public string AssignedBy { get; set; } = null!;
    public DateTime AssignedDate { get; set; }
    public string Message { get; set; } = null!;
}

public class CompleteReviewRequest
{
    public string Outcome { get; set; } = null!; // Approve, PartialDenial, Deny, Pend
    public string? Note { get; set; }
}

public class CompleteReviewResponse
{
    public int TaskId { get; set; }
    public string ClaimNumber { get; set; } = null!;
    public string Status { get; set; } = null!;
    public string Outcome { get; set; } = null!;
    public string? CompletedBy { get; set; }
    public DateTime CompletedAt { get; set; }
    public string? Note { get; set; }
    public List<CascadedTaskInfo> CascadedTasks { get; set; } = new();
    public PendTaskInfo? PendTask { get; set; }
    public string Message { get; set; } = null!;
}

public class CascadedTaskInfo
{
    public int TaskId { get; set; }
    public string Queue { get; set; } = null!;
    public string Status { get; set; } = null!;
    public DateTime ClosedAt { get; set; }
}

public class PendTaskInfo
{
    public int TaskId { get; set; }
    public string ClaimNumber { get; set; } = null!;
    public string Queue { get; set; } = null!;
    public byte Priority { get; set; }
    public DateTime DueDate { get; set; }
    public string Status { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
}
