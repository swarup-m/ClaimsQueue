namespace HealthcareClaimsQueue.API.Models;

public class UserSession
{
    public int SessionId { get; set; }
    public int UserId { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }

    public AppUser? User { get; set; }
}
