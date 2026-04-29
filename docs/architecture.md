# Architecture

## Overview

Andy Issues follows Clean Architecture with the following layers:

```
┌─────────────────────────────────────────────┐
│                Angular SPA                   │
│           (client/ directory)                │
├─────────────────────────────────────────────┤
│              API Layer                       │
│    REST Controllers │ MCP Tools │ gRPC       │
├─────────────────────────────────────────────┤
│           Application Layer                  │
│       Interfaces │ DTOs │ Contracts          │
├─────────────────────────────────────────────┤
│         Infrastructure Layer                 │
│   EF Core │ Services │ External Integrations │
├─────────────────────────────────────────────┤
│            Domain Layer                      │
│         Entities │ Enums │ Value Objects      │
└─────────────────────────────────────────────┘
```

## Project Structure

| Layer | Project | Purpose |
|-------|---------|---------|
| Domain | `Andy.Issues.Domain` | Entities, enums, value objects |
| Application | `Andy.Issues.Application` | Interfaces, DTOs, contracts |
| Infrastructure | `Andy.Issues.Infrastructure` | EF Core, service implementations |
| API | `Andy.Issues.Api` | REST, MCP, gRPC endpoints |
| Shared | `Andy.Issues.Shared` | Shared types across projects |
| CLI | `Andy.Issues.Cli` | Command-line interface |

## API Protocols

### REST (Swagger)
Standard HTTP API with OpenAPI documentation available at `/swagger`.

### MCP (Model Context Protocol)
AI assistant integration endpoint at `/mcp`. MCP tools share the same service layer as REST controllers.

### gRPC
High-performance RPC defined in `src/Andy.Issues.Api/Protos/`. gRPC services share the same service layer as REST/MCP and are introduced per-domain in Epic 9 (repositories, backlog, sandboxes).

### Real-time (SignalR)

The API exposes a SignalR hub at `/hubs/board` that pushes live backlog updates to subscribed clients. The hub requires authentication — the SignalR JavaScript client must be built with `accessTokenFactory` returning the current bearer token.

- **Hub methods** (client → server):
  - `JoinRepository(repositoryId)` — subscribes the caller's connection to the `repo-{id}` group. The hub checks `IRepositoryAccessGuard.CanViewAsync` and throws `HubException` with "Access denied" if the caller cannot see the repository.
  - `LeaveRepository(repositoryId)` — unsubscribes from the group.
- **Events** (server → client): `EpicAdded`, `EpicUpdated`, `EpicDeleted`, `FeatureAdded`, `FeatureUpdated`, `FeatureDeleted`, `StoryAdded`, `StoryUpdated`, `StoryDeleted`. Add/update events carry the full DTO; delete events carry the deleted entity's id.
- **Publication path**: `BacklogService` and the Azure DevOps pull loop depend on `IBoardNotifier` (defined in the Application layer) and publish events after every successful mutation. The API wires `IBoardNotifier` to `SignalRBoardNotifier`, a thin adapter over `IHubContext<BoardHub>`. Tests (and any future background consumers that should not broadcast) can inject `NullBoardNotifier` or a recording fake instead.
- **Scope**: the notifier layer is infrastructure-agnostic, so a later epic can swap SignalR for a message bus without touching service code.

## Messaging (NATS)

andy-issues participates in the Andy ecosystem event bus as both publisher and subscriber. The design is specified in [ADR 0001](adr/0001-messaging.md), which adopts the canonical [andy-tasks ADR 0001](https://github.com/rivoli-ai/andy-tasks/blob/main/docs/adr/0001-messaging.md) by reference. In short:

- **Commands stay on HTTP.** REST/MCP/gRPC are the command path; NATS is strictly for past-tense events.
- **Publishers write to an outbox.** Domain changes and outbox rows commit in the same EF transaction; the `OutboxDispatcher` drains rows to NATS. At-least-once delivery.
- **Consumers are idempotent.** Dedupe is by the `msg-id` header.
- **No self-subscription.** andy-issues does not listen on `andy.issues.events.*`.

**Subjects andy-issues publishes** (see ADR for payloads):
- `andy.issues.events.story.<id>.{created,readied,done,updated}`
- `andy.issues.events.issue.<id>.{triaged,accepted,rejected}`
- `andy.issues.events.repository.<id>.{registered,synced}`
- `andy.issues.events.sandbox.<id>.{attached,detached,failed}`
- `andy.issues.events.system.health` (heartbeat)

**Subjects andy-issues subscribes to:**
- `andy.containers.events.run.*.{finished,failed,cancelled}` — correlates run outcomes back to `UserStory` state. Feature-gated behind `Messaging:ConsumeRunEvents=true` until andy-containers begins publishing.

NATS vs SignalR: SignalR (`/hubs/board`) pushes backlog changes to **human clients** (the Angular app, IDE plugins). NATS carries **inter-service** domain events. They are complementary; neither replaces the other.

Implementation is tracked in Epic 15 of [`migration-stories.md`](migration-stories.md) (issues #67–#74). Default provider is `InMemory` for local dev and tests; production switches to `Nats` via `Messaging:Provider`.

## Triage Lifecycle

`Issue` is the intake envelope handled by the triage agent before any backlog item is produced. The state machine is enforced on the entity (`src/Andy.Issues.Domain/Entities/Issue.cs`) so REST, MCP (Z9), and CLI (Z10) callers all share the same invariants.

```
NeedsTriage ──Start──▶ Triaging ──Complete──▶ Triaged ──Accept──▶ Accepted (terminal)
                          ▲                       │
                          │                       └──Reject──▶ Rejected (terminal)
                          │                       │
                          └────────Start──────────┘  (re-invoke; Z9/Z10)
```

| Transition | Method on `Issue` | Endpoint | Outbox event |
|---|---|---|---|
| `NeedsTriage → Triaging` | `StartTriage()` | `POST /api/triage/{id}/start` | — |
| `Triaging → Triaged` | `CompleteTriage(by)` | `POST /api/triage/{id}/complete` | `andy.issues.events.issue.{id}.triaged` |
| `Triaged → Triaging` | `StartTriage()` | `POST /api/triage/{id}/start` | — |
| `Triaged → Accepted` | `Accept(by)` | `POST /api/triage/{id}/accept` | `andy.issues.events.issue.{id}.accepted` |
| `Triaged → Rejected` | `Reject(by)` | `POST /api/triage/{id}/reject` | `andy.issues.events.issue.{id}.rejected` |

Idempotency: `Accept` on `Accepted` (and `Reject` on `Rejected`) is a no-op — the call succeeds, `UpdatedAt` is bumped, and no duplicate outbox row is appended. Every other invalid transition throws `InvalidOperationException` (surfaced as HTTP 409).

### Triage output (Z3)

`Issue.CompleteTriage(triagedBy, output)` accepts an optional `TriageOutput` payload. When present, it is persisted as a JSON column on the `Issue` and emitted in the `andy.issues.events.issue.<id>.triaged` payload (Z4 finalises the event subject). The wire shape is frozen as v1 in [`schemas/triage-output.v1.json`](https://github.com/rivoli-ai/andy-issues/blob/main/schemas/triage-output.v1.json):

| Field | Type | Notes |
|---|---|---|
| `template_id` | enum | `bug_fix` / `feature` / `incident_response` / `upgrade` — matches andy-tasks `WorkflowTemplate` seeds (Epic AA, AA2). |
| `severity` | enum | `info` / `moderate` / `critical` — drives retention (andy-tasks AD7) + Epic AC gating. |
| `suggested_repo` | string? | Repo slug (`owner/repo`); a string rather than a `Repository` GUID because triage may propose an unknown repo. |
| `rationale` | string | Required, non-empty. Empty rationale is rejected at the entity boundary. |
| `inputs_docs_refs` | `DocsRef[]` | Echoes Z8 attachments. Inherited as `TaskNode.Inputs[]` when andy-tasks creates the Goal. |
| `initial_estimate` | `EstimateSlot` | Z7 estimator output; empty slot is well-formed until Z7 lands. Shape matches andy-tasks Epic AI. |

The schema's snake_case property names apply to the **NATS event payload** (serialised via `EventJson.Options`). The REST `POST /api/triage/{id}/complete` endpoint accepts the same record but follows the API's default camelCase convention — Z2's run-finish handler will write output via the consumer path, not REST.

`IssueEventOutbox.AppendIssueEvent` accepts optional `causationId` and `parentGeneration` parameters (Z4). When the transition is driven by an upstream message — Z2's `ContainerRunEventConsumer` reacting to a `run.finished` — the consumer passes the parent message's `msg-id` as `causationId` and its `generation` as `parentGeneration`; the resulting outbox row carries `causation-id = parent.msg-id` and `generation = parent.generation + 1` per ADR-0001 §5. User-driven transitions (REST/CLI/MCP) leave both at the default; the row is the root of its causation chain.

### Revisions (Z5)

Every change to `Issue.TriageOutput` appends a `TriageOutputRevision` row to the audit history. Two callers produce revisions:

- `CompleteTriage(output, by)` — agent-produced output; `AuthorKind = Agent`. Emits `triaged` event.
- `EditOutput(output, by)` / `Revert(targetId, by)` — human edits via `PATCH /api/triage/{id}/output` and `POST /api/triage/{id}/revert`; `AuthorKind = Human`. Emits `andy.issues.events.issue.{id}.revised`.

The `Issue.TriageOutput` field is the materialised "current" view; `TriageOutputRevisions` is the history. Human edits are allowed only while `Triaged` (rejected with HTTP 409 in any other state). Reverts produce a new revision with the target revision's content and `DiffSummary = "Reverted to revision <id>"`.

Per the AB6 reconciliation in the Z5 spec, the planning-side handoff (andy-tasks AA4) subscribes to `andy.issues.events.issue.*.accepted`, **not** `triaged` or `revised` — so AI-driven editing during human review never triggers premature Goal creation downstream.

### Input-resource attachments (Z8)

Users attach supporting documents (specs, incident dumps, screenshots, PDFs) to an issue as pointers into [`andy-docs`](https://github.com/rivoli-ai/andy-docs). The flow:

1. Conductor uploads the file payload directly to andy-docs (Epic AJ); andy-docs returns `(documentId, linkId)` where `linkId` is the typed `DocumentLink` row scoped to this issue with role `input`.
2. Conductor POSTs the pair to `andy-issues` at `POST /api/triage/{id}/attachments`. The `IDocsClient.VerifyLinkAsync` check confirms the link exists in andy-docs and targets this issue before the row is persisted.
3. The attachment is exposed on `GET /api/triage/{id}/attachments` so the UI renders inline metadata (filename, MIME) fetched via `IDocsClient.GetMetadataAsync`.

andy-issues stores **only the pointer** per the artifacts-in-andy-docs decision — the file payload never lands in this database. State gate: attachments cannot be added or removed once the issue is `Accepted` or `Rejected` (locks the input set at the handoff point).

`IDocsClient` is currently wired to `StubDocsClient`, which accepts any well-formed UUID pair and returns null metadata. The stub stays in place until andy-docs Epic AJ ships the real client; the interface contract is frozen, so the swap is a one-line DI change.

## Sandboxes and andy-containers

andy-issues never creates, execs into, or destroys containers itself. Every container operation is delegated to the sibling `andy-containers` service via its published client library.

```
┌──────────────┐  CreateContainerAsync  ┌────────────────┐   Docker / K8s
│  andy-issues │ ─────────────────────▶ │ andy-containers│ ───────────────▶  runtime
│   Sandbox    │  GetContainer / Exec   │                │
│   Service    │ ─────────────────────▶ │                │
└──────────────┘  Destroy / Connection  └────────────────┘
       │
       │ persists a thin projection
       ▼
   Sandbox rows
 (container id, repo, branch, owner, cached status)
```

- `Andy.Issues.Application.Interfaces.IContainersClient` is the seam andy-issues code depends on. `Andy.Issues.Infrastructure.External.AndyContainersClientAdapter` wraps the upstream sealed `Andy.Containers.Client.ContainersClient` so tests (and Conductor mode) can substitute a lightweight fake without pulling a real HTTP stack.
- `SandboxService` keeps only a minimal `Sandbox` projection locally: container id (the opaque identifier returned by andy-containers), repo/branch/owner, cached status, and IDE/VNC endpoints surfaced for convenience. Status is refreshed from the live container on `List`/`Get`; if the container is gone remotely the sandbox is marked `Destroyed` and eventually cleaned up.
- Deployments wire `ContainersClient` to the configured `AndyContainers:BaseUrl`. In cloud mode, an `AuthenticatedHttpHandler` forwards the caller's bearer token from the ambient `HttpContext`. In Conductor-embedded mode the `IContainersClient` binding can be supplied directly by the Conductor host so the two services share an in-process channel — no HTTP roundtrip required.

## Code Understanding and andy-code-index

andy-issues never parses, indexes, or analyzes repository source code itself. All code understanding — symbol search, file analysis, repository summarization, draft-backlog generation — is delegated to the sibling `andy-code-index` service via its HTTP client.

- `Andy.Issues.Application.Interfaces.ICodeIndexClient` is the seam andy-issues code depends on. Repositories are auto-registered with `andy-code-index` on creation (`Story 6.2`) and the draft-backlog generator (`Story 6.3`) reads indexed context from the same service.
- The legacy in-repo code analysis shapes (`CodeAnalysis`, `FileAnalysis`, `VPSAnalysisService`) have been removed. A CI guard in `.github/workflows/ci.yml` fails the build if any of those identifiers reappear in `src/`, `tests/`, `tools/`, or `client/src/`.

## Database Strategy

- **PostgreSQL** (default): Used in standalone deployment
- **SQLite** (embedded): Used when bundled with Conductor

Configured via `Database:Provider` in appsettings or environment variable.

## Authentication Flow

```
User → Angular SPA → Andy Auth (OIDC) → JWT Token → API (Bearer Auth)
```

## External Dependencies

- **Andy Auth** (port 5001) - OAuth2/OIDC identity provider
- **Andy RBAC** (port 5003) - Role-based access control
- **Andy Settings** (port 5300) - Centralized configuration (optional)
