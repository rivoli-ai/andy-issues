using Andy.Issues.Api.Auth;
using Andy.Issues.Application.Dtos;
using Andy.Issues.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Andy.Issues.Api.Controllers;

// Shared by the creation route and profile routes; expected validation errors are HTTP 400.
public sealed class AgentRuleValidationFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is AgentRuleValidationException e)
        { context.Result = new BadRequestObjectResult(new { error = e.Message }); context.ExceptionHandled = true; }
    }
}

[ApiController, Authorize]
public class AgentRuleProfilesController(IAgentRuleProfiles profiles) : ControllerBase
{
    private string UserId => User.RequireUserId();
    [HttpGet("api/repositories/{repositoryId:guid}/agent-rules/profiles")]
    public async Task<IActionResult> List(Guid repositoryId, CancellationToken ct) =>
        await profiles.ListAsync(repositoryId, UserId, ct) is { } result ? Ok(result) : NotFound();
    [HttpPost("api/repositories/{repositoryId:guid}/agent-rules")]
    public async Task<IActionResult> Create(Guid repositoryId, AgentRuleWriteRequest request, CancellationToken ct) =>
        await profiles.WriteAsync(repositoryId, UserId, "create", request, ct: ct) is { } result ? Ok(result) : NotFound();
    [HttpPut("api/repositories/{repositoryId:guid}/agent-rules/{ruleId:guid}")]
    public async Task<IActionResult> Update(Guid repositoryId, Guid ruleId, AgentRuleWriteRequest request, CancellationToken ct) =>
        await profiles.WriteAsync(repositoryId, UserId, "update", request, ruleId, ct: ct) is { } result ? Ok(result) : NotFound();
    [HttpDelete("api/repositories/{repositoryId:guid}/agent-rules/{ruleId:guid}")]
    public async Task<IActionResult> Delete(Guid repositoryId, Guid ruleId, CancellationToken ct) =>
        await profiles.WriteAsync(repositoryId, UserId, "delete", ruleId: ruleId, ct: ct) is { } result ? Ok(result) : NotFound();
    [HttpPost("api/repositories/{repositoryId:guid}/agent-rules/replace")]
    public async Task<IActionResult> Replace(Guid repositoryId, IReadOnlyList<AgentRuleWriteRequest> request, CancellationToken ct) =>
        await profiles.WriteAsync(repositoryId, UserId, "replace", replacement: request, ct: ct) is { } result ? Ok(result) : NotFound();
    [HttpPost("api/agent-rules/{ruleId:guid}/set-default")]
    public async Task<IActionResult> SetDefault(Guid ruleId, CancellationToken ct) =>
        await profiles.SetDefaultAsync(ruleId, UserId, ct) ? NoContent() : NotFound();
    [HttpPut("api/stories/{storyId:guid}/agent-rule")]
    public async Task<IActionResult> Select(Guid storyId, StoryAgentRuleRequest request, CancellationToken ct) =>
        await profiles.SelectAsync(storyId, request.AgentRuleId, UserId, ct) ? NoContent() : NotFound();
    [HttpGet("api/stories/{storyId:guid}/effective-agent-rules")]
    public async Task<IActionResult> Effective(Guid storyId, CancellationToken ct) =>
        await profiles.EffectiveAsync(storyId, UserId, ct) is { } result ? Ok(result) : NotFound();
}
