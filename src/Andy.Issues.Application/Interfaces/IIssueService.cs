// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Dtos;
using Andy.Issues.Application.Requests;

namespace Andy.Issues.Application.Interfaces;

// Triage lifecycle for an Issue (Z1). The four transition methods are
// the single entry point for state changes — REST, MCP (Z9), and CLI
// (Z10) all flow through here so the state machine and outbox writes
// stay consistent.
public interface IIssueService
{
    Task<IssueDto?> GetAsync(Guid id, string userId, CancellationToken ct = default);

    // Z1 transition methods. See Issue.cs for the state machine.
    Task<IssueTriageResult> StartTriageAsync(Guid id, string userId, CancellationToken ct = default);
    Task<IssueTriageResult> CompleteTriageAsync(Guid id, string userId, CancellationToken ct = default);
    Task<IssueTriageResult> AcceptAsync(Guid id, string userId, CancellationToken ct = default);
    Task<IssueTriageResult> RejectAsync(Guid id, string userId, CancellationToken ct = default);

    // Minimal create — needed to drive the state machine end-to-end. The
    // production intake path (which is out of scope for Z1) will likely
    // come from external sources (GitHub webhooks, conductor) once
    // wired up.
    Task<IssueDto> CreateAsync(CreateIssueRequest request, string userId, CancellationToken ct = default);
}
