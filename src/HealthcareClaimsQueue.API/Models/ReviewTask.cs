namespace HealthcareClaimsQueue.API.Models;

public class ReviewTask
{
    public int TaskId { get; set; }
    public int ClaimId { get; set; }
    public int QueueId { get; set; }

    public byte Priority { get; set; } = 1; // 1-5, Lower = higher priority
    public DateTime DueDate { get; set; }
    public string Status { get; set; } = "Open"; // Open, Closed
    public string? Outcome { get; set; } // Approve, PartialDenial, Deny, Pend, SystemClosed, or NULL

    // Assignment
    public int? AssignedToUserId { get; set; }
    public int? AssignedByUserId { get; set; }

    // Lock tracking
    public int? LockedByUserId { get; set; }
    public DateTime? LockedOn { get; set; }
    public DateTime? LockExpiresOn { get; set; }

    // Completion tracking
    public DateTime? CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ClosedAt { get; set; }
    public int? ClosedByUserId { get; set; } // NULL for cascade closures
    public string? Note { get; set; }
    public DateTime? PendedAt { get; set; }

    // Navigation properties
    public Claim? Claim { get; set; }
    public Queue? Queue { get; set; }
    public AppUser? AssignedToUser { get; set; }
    public AppUser? AssignedByUser { get; set; }
    public AppUser? LockedByUser { get; set; }
    public AppUser? ClosedByUser { get; set; }
}
