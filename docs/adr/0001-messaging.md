# ADR 0001 — Messaging in andy-issues

- **Status:** Accepted
- **Date:** 2026-04-14
- **Deciders:** Sami Ben Grine
- **Scope:** andy-issues only. This is a reference/adoption ADR; the canonical ecosystem decision lives in [`andy-tasks/docs/adr/0001-messaging.md`](https://github.com/rivoli-ai/andy-tasks/blob/main/docs/adr/0001-messaging.md).

## Decision

andy-issues adopts the ecosystem messaging contract defined in andy-tasks's ADR 0001 (2026-04-10, amended 2026-04-14 for the andy-devpilot split). The rules — commands on HTTP, events on NATS; transactional outbox per publisher; headers (`msg-id`, `correlation-id`, `causation-id`, `generation`); no self-subscription; DLQ on `andy.<service>.dlq.<subject>` — apply here unchanged.

This document records what is **specific to andy-issues**: the subjects it owns, the subjects it consumes, and the rollout sequence.

## Subjects andy-issues publishes

| Subject | Emitted by | Notes |
|---|---|---|
| `andy.issues.events.story.<id>.created` | `BacklogService` | On `UserStory` insert. |
| `andy.issues.events.story.<id>.readied` | `BacklogService` | On transition to `Ready`. |
| `andy.issues.events.story.<id>.done` | `BacklogService` | On transition to `Done`. |
| `andy.issues.events.story.<id>.updated` | `BacklogService` | On any other change. |
| `andy.issues.events.issue.<id>.triaged` | `IssueService` | Z4 — on `Triaging → Triaged` transition. Payload includes the full `TriageOutput` (Z3). Causation chain populated when triggered by a `run.finished` (Z2). |
| `andy.issues.events.issue.<id>.accepted` | `IssueService` | On `Triaged → Accepted` transition (terminal). |
| `andy.issues.events.issue.<id>.rejected` | `IssueService` | On `Triaged → Rejected` transition (terminal). |
| `andy.issues.events.repository.<id>.registered` | `RepositoryService` | On `Repository` creation via any surface. |
| `andy.issues.events.repository.<id>.synced` | `BacklogGitHubImportService`, AzDo sync | On successful sync only. Failures stay in the REST response DTO. |
| `andy.issues.events.sandbox.<id>.attached` | `SandboxService` | Local projection of an andy-containers container attached to a story. |
| `andy.issues.events.sandbox.<id>.detached` | `SandboxService` | Sandbox destroyed/released. |
| `andy.issues.events.sandbox.<id>.failed` | `SandboxService` | Provisioning error captured locally. |
| `andy.issues.events.system.health` | Heartbeat worker | Every 30s, per ecosystem ADR §6. |

Payloads are JSON with a `schema_version: 1` field. See each story's acceptance criteria (Epic 15 in `docs/migration-stories.md`) for the exact shape.

## Subjects andy-issues subscribes to

| Subject | Handler | Behavior |
|---|---|---|
| `andy.containers.events.run.*.finished` | `ContainerRunEventConsumer` | Transitions the correlated `UserStory` to `InReview`. |
| `andy.containers.events.run.*.failed` | `ContainerRunEventConsumer` | Keeps the story in `InProgress`; appends a note to its activity log. |
| `andy.containers.events.run.*.cancelled` | `ContainerRunEventConsumer` | Same as `failed`. |

The consumer is durable (`andy-issues-run-events`) and idempotent per `msg-id`. It is feature-gated behind `Messaging:ConsumeRunEvents=true` so it stays off until andy-containers actually publishes these events (coordinated through andy-tasks ADR Phase 3).

## Rules restated

These come from the parent ADR and are load-bearing here:

- **Outbox is the single source of truth** for "what needs to be published." Domain writes and outbox appends happen in the **same** EF transaction.
- **No self-subscription.** andy-issues must never subscribe to its own `andy.issues.events.*` namespace, even on a different subject. Lint/review enforces this.
- **Commands stay on HTTP.** Submitting a run to andy-containers, asking andy-code-index for context, etc., remains REST. Events are the only thing on NATS.
- **Idempotency is the consumer's job.** Every subscriber dedupes by `msg-id`.

## Rollout

See Epic 15 in [`docs/migration-stories.md`](../migration-stories.md). Summary:

1. **Story 15.2** — Outbox + `IMessageBus` abstraction + `InMemoryMessageBus`. No NATS yet.
2. **Stories 15.3 – 15.5** — Publishers, InMemory bus, full test coverage.
3. **Story 15.7** — `NatsMessageBus` behind `Messaging:Provider=Nats` configuration.
4. **Story 15.8** — Env-gated NATS integration tests + CI job.
5. **Story 15.6** — Subscribe to andy-containers run events. Gated; does nothing until andy-containers publishes (andy-tasks ADR Phase 3, cross-repo).

Until Story 15.7 lands, the default `Messaging:Provider=InMemory` means the outbox dispatcher publishes to an in-process subscriber only. That is sufficient for local development and tests; the contract is unchanged when NATS is switched on.

## References

- [andy-tasks ADR 0001 — Messaging in the Andy Ecosystem](https://github.com/rivoli-ai/andy-tasks/blob/main/docs/adr/0001-messaging.md) (canonical).
- [`docs/architecture.md`](../architecture.md) — "Messaging" section (links here).
- GitHub Epic 15: issues #67–#74.
