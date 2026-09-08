namespace Andy.Issues.Domain.Entities;

public class AgentRule
{
    public Guid Id { get; set; }
    public Guid RepositoryId { get; set; }
    public Repository Repository { get; set; } = null!;
    public string Name { get; set; } = "Default";
    public string NameKey { get; set; } = "DEFAULT";
    public string Body { get; set; } = "";
    public bool IsDefault { get; set; }
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
}
