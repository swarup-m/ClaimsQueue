namespace HealthcareClaimsQueue.API.Models;

public class Claim
{
    public int ClaimId { get; set; }
    public string ClaimNumber { get; set; } = null!;
    public string MemberId { get; set; } = null!;
    public string ProviderId { get; set; } = null!;
    public decimal BilledAmount { get; set; }
    public DateTime ServiceFrom { get; set; }
    public DateTime ServiceTo { get; set; }
    public DateTime ReceivedOn { get; set; }

    public ICollection<ReviewTask> ReviewTasks { get; set; } = new List<ReviewTask>();
}
