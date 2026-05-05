// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Dtos;

namespace Andy.Issues.Application.Interfaces;

public enum AgentRulesGetOutcome
{
    Ok = 0,
    NotFound = 1
}

public enum AgentRulesUpdateOutcome
{
    Updated = 0,
    NotFound = 1,
    NotOwner = 2,
    TooLarge = 3
}

public interface IAgentRulesService
{
    Task<(AgentRulesGetOutcome Outcome, AgentRulesDto? Dto)> GetAsync(
        Guid repositoryId,
        string userId,
        CancellationToken ct = default);

    Task<AgentRulesUpdateOutcome> UpdateAsync(
        Guid repositoryId,
        string rules,
        string ownerUserId,
        CancellationToken ct = default);
}
