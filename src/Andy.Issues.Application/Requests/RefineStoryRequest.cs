// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Andy.Issues.Application.Requests;

// SP.0.4 (andy-issues#180 / conductor#1632) — body of
// POST /api/stories/{id}/refine. Both fields are optional:
//
//   • Instructions — free-text amendments to the prompt. When null the
//     agent runs against the story's existing title/description/labels/
//     AC alone (conductor's RefinementPanel passes empty by default).
//   • AgentId       — which triage agent to invoke. When null we resolve
//     the workspace default via IAgentsClient.GetTriageAgentAsync (Z2
//     pattern shared with the issue-side triage flow).
public sealed record RefineStoryRequest(
    string? Instructions = null,
    string? AgentId = null);
