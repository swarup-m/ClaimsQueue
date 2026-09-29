namespace HealthcareClaimsQueue.API.Models;

public class Queue
{
    public int QueueId { get; set; }
    public string QueueCode { get; set; } = null!;
    public string QueueName { get; set; } = null!;

    public ICollection<ReviewTask> ReviewTasks { get; set; } = new List<ReviewTask>();
}
