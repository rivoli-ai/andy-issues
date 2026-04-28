// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Andy.Issues.Domain.Enums;

// Categorical severity (Z3). Drives retention (rivoli-ai/andy-tasks
// AD7 critical-issue marker) and Epic AC gating downstream. Three
// levels keep triage-time decisions cheap; finer-grained severity
// belongs on the Goal once execution constraints are known.
public enum TriageSeverity
{
    Info = 0,
    Moderate = 1,
    Critical = 2
}
