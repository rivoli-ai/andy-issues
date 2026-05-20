// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Domain.Enums;

namespace Andy.Issues.Application.Requests;

// #187 — single parser for the `GET /api/issues` query string. Lives
// in Application (not the controller) so the same validation runs from
// REST, MCP, and any future caller that needs to compose state +
// assignee filters.
//
// Validation contract — unlike the legacy `GET /api/triage` endpoint
// (which fails soft on unknown filter values), `/api/issues` 400s on
// any unknown state or unknown assignee form. Conductor declares one
// typed protocol method for both consumer panes; a typo in the CSV
// should surface as an error there, not as a silently-empty page that
// looks like "no rows match" to the user.
public static class IssueListQueryParser
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 200;

    public static bool TryParse(
        string? state,
        string? assignee,
        string? authenticatedUserId,
        Guid? repository,
        int? limit,
        string? cursor,
        out IssueListQuery query,
        out string? error)
    {
        query = null!;
        error = null;

        IReadOnlyList<TriageState>? states = null;
        if (!string.IsNullOrWhiteSpace(state))
        {
            var pieces = state.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var parsed = new List<TriageState>(pieces.Length);
            foreach (var piece in pieces)
            {
                if (!TryParseState(piece, out var value))
                {
                    error = $"Unknown state '{piece}'. Allowed: {AllowedStates()}.";
                    return false;
                }

                if (!parsed.Contains(value)) parsed.Add(value);
            }

            if (parsed.Count > 0) states = parsed;
        }

        var assigneeFilter = AssigneeFilter.Unfiltered;
        if (!string.IsNullOrWhiteSpace(assignee))
        {
            if (string.Equals(assignee, "none", StringComparison.OrdinalIgnoreCase))
            {
                assigneeFilter = AssigneeFilter.None;
            }
            else if (string.Equals(assignee, "me", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrEmpty(authenticatedUserId))
                {
                    error = "assignee=me requires an authenticated principal.";
                    return false;
                }
                assigneeFilter = AssigneeFilter.ForUser(authenticatedUserId);
            }
            else
            {
                // Treat as a literal user id. Sanity-check length so we
                // don't pass an arbitrarily-long string through to EF.
                if (assignee.Length > 256)
                {
                    error = "assignee user id exceeds 256 characters.";
                    return false;
                }
                assigneeFilter = AssigneeFilter.ForUser(assignee);
            }
        }

        var clampedLimit = limit is null
            ? DefaultLimit
            : Math.Clamp(limit.Value, 1, MaxLimit);

        query = new IssueListQuery(states, assigneeFilter, repository, clampedLimit, cursor);
        return true;
    }

    // Accept both the canonical `needs-triage` wire form and the
    // PascalCase enum name (`NeedsTriage`). The wire form mirrors the
    // issue spec verbatim; the PascalCase form keeps the legacy MCP/CLI
    // surface working when callers route through the new endpoint.
    private static bool TryParseState(string raw, out TriageState value)
    {
        var normalised = raw.Replace("-", string.Empty);
        return Enum.TryParse(normalised, ignoreCase: true, out value);
    }

    private static string AllowedStates() =>
        string.Join(", ", new[]
        {
            "needs-triage", "triaging", "triaged", "accepted", "rejected"
        });
}
