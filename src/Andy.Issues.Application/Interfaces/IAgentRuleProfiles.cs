using Andy.Issues.Application.Dtos;
namespace Andy.Issues.Application.Interfaces;

public sealed class AgentRuleValidationException(string message) : Exception(message);
public interface IAgentRuleProfiles
{
    Task<IReadOnlyList<AgentRuleProfileDto>?> ListAsync(Guid repositoryId, string userId, CancellationToken ct = default);
    Task<IReadOnlyList<AgentRuleProfileDto>?> WriteAsync(Guid repositoryId, string userId, string operation,
        AgentRuleWriteRequest? request = null, Guid? ruleId = null, IReadOnlyList<AgentRuleWriteRequest>? replacement = null, CancellationToken ct = default);
    Task<bool> SetDefaultAsync(Guid ruleId, string userId, CancellationToken ct = default);
    Task<EffectiveAgentRulesDto?> EffectiveAsync(Guid storyId, string userId, CancellationToken ct = default);
    Task<bool> SelectAsync(Guid storyId, Guid? ruleId, string userId, CancellationToken ct = default);
}
