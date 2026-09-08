using System.ComponentModel;
using System.Text.Json;
using Andy.Issues.Api.Auth;
using Andy.Issues.Application.Dtos;
using Andy.Issues.Application.Interfaces;
using ModelContextProtocol.Server;
namespace Andy.Issues.Api.Mcp;

[McpServerToolType]
public static class AgentRuleTools
{
    private static string User(IHttpContextAccessor ctx) => ctx.HttpContext!.User.RequireUserId();
    private static string Json(object? value) => JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    [McpServerTool, Description("List the named agent-rule profiles for a repository.")]
    public static async Task<string> ListAgentRules(IHttpContextAccessor ctx, IAgentRuleProfiles svc, Guid repositoryId) =>
        Json(await svc.ListAsync(repositoryId, User(ctx)));
    [McpServerTool, Description("Create a named agent-rule profile. Repository owner only.")]
    public static async Task<string> CreateAgentRule(IHttpContextAccessor ctx, IAgentRuleProfiles svc, Guid repositoryId,
        string name, string body, bool isDefault = false, int sortOrder = 0) =>
        Json(await svc.WriteAsync(repositoryId, User(ctx), "create", new(name, body, isDefault, sortOrder)));
    [McpServerTool, Description("Update a named agent-rule profile. Repository owner only.")]
    public static async Task<string> UpdateAgentRule(IHttpContextAccessor ctx, IAgentRuleProfiles svc, Guid repositoryId,
        Guid ruleId, string name, string body, bool isDefault = false, int sortOrder = 0) =>
        Json(await svc.WriteAsync(repositoryId, User(ctx), "update", new(name, body, isDefault, sortOrder), ruleId));
    [McpServerTool, Description("Delete a named rule profile; affected stories fall back to the repository default.")]
    public static async Task<string> DeleteAgentRule(IHttpContextAccessor ctx, IAgentRuleProfiles svc, Guid repositoryId, Guid ruleId) =>
        Json(await svc.WriteAsync(repositoryId, User(ctx), "delete", ruleId: ruleId));
    [McpServerTool, Description("Resolve a story's rules: selected profile, repository default, then system default.")]
    public static async Task<string> GetEffectiveAgentRules(IHttpContextAccessor ctx, IAgentRuleProfiles svc, Guid storyId) =>
        Json(await svc.EffectiveAsync(storyId, User(ctx)));
}
