// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Andy.Issues.Api.Telemetry;

/// <summary>
/// Domain <see cref="ActivitySource"/> and <see cref="Meter"/> for andy-issues.
///
/// Wired via Andy.Telemetry in <c>Program.cs</c> (OT5 —
/// rivoli-ai/conductor#1263). Code that wants to emit issues-specific
/// spans (backlog sync, triage estimation, repository import) calls
/// <see cref="ActivitySource"/>; domain counters/histograms hang off
/// <see cref="Meter"/>.
/// </summary>
public static class IssuesTelemetry
{
    /// <summary>Activity source name. Matches the registration in <c>AddAndyTelemetry</c>.</summary>
    public const string ActivitySourceName = "Andy.Issues";

    /// <summary>Meter name. Matches the registration in <c>AddAndyTelemetry</c>.</summary>
    public const string MeterName = "Andy.Issues";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    public static readonly Meter Meter = new(MeterName);
}
