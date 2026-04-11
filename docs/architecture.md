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
