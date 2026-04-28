# ADR 0002 — Triage belongs to andy-issues

- **Status:** Accepted
- **Date:** 2026-04-28
- **Deciders:** Sami Ben Grine
- **Scope:** Cross-service. The decision binds `andy-issues`, `andy-tasks`, `andy-agents`, `andy-containers`, `andy-docs`, and `conductor`. Recorded here because andy-issues is the owning service.

## Context

Triage — turning a freshly filed issue into a classified, estimated, accepted/rejected work item — sits at the seam between intake and execution. Two reasonable homes exist:

1. **andy-tasks**, on the grounds that triage produces a Goal/TaskNode and the task graph is its native domain.
2. **andy-issues**, on the grounds that triage operates on an `Issue` (the intake envelope) and the lifecycle ends before any task is created.

Naming alone does not settle the question — andy-tasks could host an `Issue` shape, and andy-issues could host an early Goal. The decision matters because every downstream consumer (conductor's issue-detail UI, andy-agents' triage agent, andy-tasks' planner) needs a single owner to coordinate against.

## Decision

**Triage lives in andy-issues.**

Concretely:

- The `Issue` entity, the `TriageState` enum, the state machine (`NeedsTriage → Triaging → Triaged → Accepted | Rejected`), the persistence layer, and the event emission all live in `andy-issues`.
- The triage **agent definition** lives in `andy-agents` (Epic W, story W5). The agent runs **headless in andy-containers**.
- `andy-tasks` is a **consumer**: it subscribes to `andy.issues.events.issue.{id}.triaged` and creates a Goal from the payload. It never writes Issue state.
- Human-in-the-loop edits (accept/reject, output overrides per Z5) happen in `conductor`'s issue-detail UI, hitting andy-issues' REST surface.
- The per-tenant **cost/time estimator** (Z7) is owned by andy-issues — it consumes andy-issues' own historical triage outcomes and is queried at triage-time. The result is materialised into the Goal's `initial` estimate slot when andy-tasks creates the Goal (consumer-side fanout).

## Rationale

1. **Service-boundary principle.** Each service owns one mental model. andy-tasks owns the task graph (Goals, TaskNodes, dependencies, execution runs). andy-issues owns intake (filing, classification, attachments, triage). Folding triage into andy-tasks would conflate "what should we do" with "how is it being done" — the boundary that makes both services worth having separately.

2. **Lifecycle phase mismatch.** Every triage transition happens **before** a Goal exists. Putting triage in andy-tasks would force andy-tasks to model a pre-Goal state, which is exactly the responsibility andy-issues already has.

3. **Mental model for users.** "I filed an issue; it got triaged" maps to the issue tracker most users already hold in their heads. "I filed a task that is being triaged" is awkward.

4. **Single coordination point for cross-service work.** All consumers (conductor for UI, andy-tasks for goal creation, andy-docs for input/output artifacts) coordinate against one publisher's event stream rather than negotiating a shared schema between two writers.

## Consequences

### Owned by andy-issues
- `Issue` entity + `TriageState` (Z1).
- Triage agent invocation through `IContainersClient` (Z2).
- Triage output schema — classification, severity, suggested repo, rationale (Z3).
- Outbox subjects: `andy.issues.events.issue.<id>.{triaged,accepted,rejected}` (Z4).
- Audit log of triage runs (Z6) — payloads stored as andy-docs refs per the artifacts-in-andy-docs decision.
- Per-tenant cost/time estimator (Z7).
- Input-resource attachments — `DocsRef[]` keyed off the `Issue`, inherited as `TaskNode.Inputs[]` when andy-tasks creates the Goal (Z8).
- MCP tools `issue_get`/`issue_list`/`issue_triage` (Z9) and CLI `andy-issues-cli issues {list,get,triage}` (Z10).

### Owned by sibling services
- **andy-agents** — the triage agent definition (skill, prompts, tool affordances) per Epic W (story W5).
- **andy-containers** — the headless run that executes the agent. Receives `{agent_id, issue_id, tenant_id, input_doc_refs}` from andy-issues and emits run events on `andy.containers.events.run.<id>.*`.
- **andy-docs** — the artifact store. Triage inputs and outputs are stored as `DocsRef`s per the artifacts-in-andy-docs decision (memory note `project_artifacts_in_andy_docs.md`); andy-issues only stores pointers.
- **andy-tasks** — subscribes to `andy.issues.events.issue.*.triaged`; creates a Goal whose first `TaskNode.Inputs[]` is inherited from the Issue's input docs (Epic AA). Never writes Issue state.
- **conductor** — issue-detail UI (Epic AB) renders the triage output, supports human edit before `accepted`, allows re-invoke. Re-invoke calls andy-issues' REST surface; conductor never bypasses the state machine.

### Not done
- **No self-subscription on `andy.issues.events.issue.*`.** Per ADR-0001 §3, andy-issues never consumes its own namespace. The triage state transitions are driven by REST/MCP/CLI commands plus the run-completion event from andy-containers.
- **No issue-state writes from andy-tasks or conductor.** Both services are read/command consumers only; state changes flow through andy-issues' REST surface.

## Alternatives considered

### A. Triage lives in andy-tasks
Rejected. Forces andy-tasks to model a pre-Goal phase; conflates intake with execution; complicates the conductor UI which would need to span two service boundaries for a single user task.

### B. Triage as its own service (`andy-triage`)
Rejected. Triage is small (one entity, one state machine, one agent invocation, one event subject) and tightly coupled to intake. A dedicated service would multiply infra cost (auth, RBAC, settings, telemetry, deployment) without a corresponding ownership boundary.

### C. Triage state on `UserStory`, no `Issue` entity
Rejected during Z1 design. Overloads `UserStory` with two unrelated state machines (backlog progress + intake triage) and forces every backlog query to filter on triage state. The new entity is cleaner and lets `UserStory` stay focused on the backlog-item lifecycle.

## References

- [ADR 0001 — Messaging in andy-issues](0001-messaging.md) — outbox + subject taxonomy this ADR builds on.
- [`docs/architecture.md`](../architecture.md#triage-lifecycle) — runtime sequence + state diagram.
- [`docs/features.md`](../features.md#triage-workflow) — REST/MCP/CLI surface tables.
- Epic Z (#108) — full cross-service plan and story breakdown.
- Architecture memos: `project_triage_planning_architecture.md`, `project_service_boundaries.md`, `project_artifacts_in_andy_docs.md`.
