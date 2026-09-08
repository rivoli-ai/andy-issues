namespace Andy.Issues.Domain.Entities;

public class RecategorizationJob
{
    public Guid Id { get; set; }
    public Guid RepositoryId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string Phase { get; set; } = "CollectingItems";
    public string? ResultJson { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
}
