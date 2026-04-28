// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Andy.Issues.Domain.Enums;

// Triage classification (Z3). The four ids match andy-tasks
// WorkflowTemplate seeds (Epic AA, story AA2) byte-for-byte so the
// triage→planning subscription (AA4) can pre-select a template without
// a translation table.
public enum TriageTemplateId
{
    BugFix = 0,
    Feature = 1,
    IncidentResponse = 2,
    Upgrade = 3
}
