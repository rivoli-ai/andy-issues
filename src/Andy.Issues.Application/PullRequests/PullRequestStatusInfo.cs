// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Andy.Issues.Application.PullRequests;

// Provider-agnostic snapshot of a PR's lifecycle. State is normalised
// to "open" | "closed" | "merged" so the rest of the codebase doesn't
// have to care which provider answered. HeadBranch is included for
// #90 (resolve a PR URL to its head branch) — both GitHub and Azure
// DevOps return it on the same fetch we do for status, so carrying it
// through avoids a second round-trip.
public sealed record PullRequestStatusInfo(
    string State,
    bool Merged,
    DateTimeOffset? MergedAt,
    string HeadBranch);
