// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Andy.Issues.Application.Messaging.Events;

/// <summary>
/// AH6 (rivoli-ai/conductor#713) — consumer-side projection of the
/// <c>andy.tasks.events.goal.{id}.created</c> payload emitted by
/// andy-tasks at <c>SchemaVersion &gt;= 2</c>. The
/// <see cref="Andy.Issues.Infrastructure.Messaging.Consumers.GoalLinkageConsumer"/>
/// reads <see cref="SourceIssueDisplayId"/> + <see cref="GoalDisplayId"/>
/// to write the <c>Issue.GoalDisplayId</c> reverse pin.
/// </summary>
/// <remarks>
/// Forward-compatible: extra fields on the producer side are ignored.
/// The consumer only requires the three fields it actually uses
/// (<see cref="GoalId"/>, <see cref="GoalDisplayId"/>,
/// <see cref="SourceIssueDisplayId"/>) — every other field on the
/// upstream <c>GoalCreatedEvent</c> is irrelevant to the linkage
/// write.
/// </remarks>
public sealed record GoalCreatedEventPayload(
    Guid GoalId,
    string? GoalDisplayId,
    string? SourceIssueDisplayId);
