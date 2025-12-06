namespace Matriarch.Web.Models;

public class ScrumTask
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public ScrumTaskStatus Status { get; set; } = ScrumTaskStatus.New;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public enum ScrumTaskStatus
{
    New,
    ToDo,
    InProgress,
    Completed
}
