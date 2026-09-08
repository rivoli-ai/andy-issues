namespace Andy.Issues.Application.Dtos;

public record AgentRuleProfileDto(Guid Id, Guid RepositoryId, string Name, string Body,
    bool IsDefault, int SortOrder, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt);
public record AgentRuleWriteRequest(string Name, string Body, bool IsDefault = false, int SortOrder = 0, Guid? Id = null);
public record EffectiveAgentRulesDto(string Rules, string Source, Guid? AgentRuleId, string? Name);
public record StoryAgentRuleRequest(Guid? AgentRuleId);
