namespace HealthcareClaimsQueue.API.Models;

public class AppUser
{
    public int UserId { get; set; }
    public string Username { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public string Role { get; set; } = null!; // "Supervisor" or "Reviewer"

    public ICollection<ReviewTask> AssignedTasks { get; set; } = new List<ReviewTask>();
    public ICollection<ReviewTask> LockedTasks { get; set; } = new List<ReviewTask>();
    public ICollection<ReviewTask> CompletedTasks { get; set; } = new List<ReviewTask>();
    public ICollection<UserSession> UserSessions { get; set; } = new List<UserSession>();
}
