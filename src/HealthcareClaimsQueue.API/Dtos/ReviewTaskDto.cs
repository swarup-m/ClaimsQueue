namespace HealthcareClaimsQueue.API.Dtos;

public class ReviewTaskDto
{
    public int TaskId { get; set; }
    public string ClaimNumber { get; set; } = null!;
    public string Queue { get; set; } = null!;
    public byte Priority { get; set; }
    public DateTime DueDate { get; set; }
    public string? AssignedTo { get; set; }
    public string? AssignedBy { get; set; }
    public DateTime? AssignedDate { get; set; }
    public string? LockedBy { get; set; }
    public DateTime? LockedUntil { get; set; }
    public string Status { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
}

public class PagedReviewTasksResponse
{
    public List<ReviewTaskDto> Data { get; set; } = new();
    public PageInfo PageInfo { get; set; } = null!;
}

public class PageInfo
{
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
}

public class GetNextClaimResponse
{
    public int TaskId { get; set; }
    public string ClaimNumber { get; set; } = null!;
    public string Queue { get; set; } = null!;
    public byte Priority { get; set; }
    public DateTime DueDate { get; set; }
    public string? AssignedTo { get; set; }
    public string? LockedBy { get; set; }
    public int DaysUntilDue { get; set; }
}
