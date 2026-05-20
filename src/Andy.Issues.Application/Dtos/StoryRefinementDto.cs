// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Andy.Issues.Application.Dtos;

// SP.0.4 (andy-issues#180 / conductor#1632) — wire shape of the
// refinement output produced by POST /api/stories/{id}/refine.
//
// Conductor's `StoryRefinementDTO` decoder consumes this shape; it is
// also embedded in the `andy.issues.events.story.{id}.triaged` outbox
// payload so the chat panel can transition the local TriageState
// reactively without re-fetching the story.
//
// Wire shape (camelCase via the ASP.NET Core default JSON options on
// REST controllers; snake_case via EventJson.Options on the outbox row):
//
//   {
//     "refinedDescription": "string",
//     "acceptanceCriteria": ["string"],
//     "risks": ["string"],
//     "testPlan": ["string"],
//     "classification": {
//       "priority": "p0|p1|p2|p3",
//       "complexity": "trivial|small|medium|large|xl",
//       "risk": "low|medium|high",
//       "suggestedApproach": "string"
//     },
//     "refineVersion": 1,
//     "refinedAt": "2026-05-19T20:00:00Z",
//     "refinedBy": "userId-or-agentId"
//   }
public sealed record StoryRefinementDto(
    string? RefinedDescription,
    IReadOnlyList<string> AcceptanceCriteria,
    IReadOnlyList<string> Risks,
    IReadOnlyList<string> TestPlan,
    StoryClassificationDto Classification,
    int RefineVersion,
    DateTimeOffset RefinedAt,
    string RefinedBy);

public sealed record StoryClassificationDto(
    StoryPriorityWire Priority,
    StoryComplexityWire Complexity,
    StoryRiskWire Risk,
    string? SuggestedApproach);

// Wire-level enum companions: lowercase via the JsonStringEnumConverter
// registered on every JSON surface (`AddControllers().AddJsonOptions`
// for REST, `EventJson.Options` for outbox payloads). Distinct from the
// domain enums so the wire vocabulary ("p0" / "xl") is decoupled from
// the .NET-friendly PascalCase identifiers.
public enum StoryPriorityWire
{
    p0,
    p1,
    p2,
    p3
}

public enum StoryComplexityWire
{
    trivial,
    small,
    medium,
    large,
    xl
}

public enum StoryRiskWire
{
    low,
    medium,
    high
}
