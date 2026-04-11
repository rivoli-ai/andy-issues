# Features

## API

- **REST API** - Full CRUD operations with Swagger/OpenAPI documentation
- **gRPC** - High-performance RPC for service-to-service communication
- **MCP** - Model Context Protocol tools for AI assistant integration

## Frontend

- **Angular SPA** - Modern single-page application built with Angular 18
- **OIDC Authentication** - Integrated with Andy Auth for SSO
- **Responsive Design** - Works across desktop and mobile

## Security

- **OAuth2/OIDC** - Authentication via Andy Auth server
- **RBAC** - Role-based access control via Andy RBAC
- **HTTPS Everywhere** - TLS from development to production
- **JWT Bearer** - API authentication with JWT tokens

## Data

- **PostgreSQL** - Primary database for production and development
- **SQLite** - Embedded database for Conductor integration
- **EF Core** - Entity Framework Core for data access with migrations

## Operations

- **OpenTelemetry** - Distributed tracing, metrics, and logging
- **Docker** - Containerized deployment with multi-stage builds
- **Health Checks** - Built-in health endpoint at `/health`
- **CI/CD** - GitHub Actions for build, test, and deployment

## CLI

- **Command-line interface** - Manage resources from the terminal
- **Token-based auth** - Works with Andy Auth Bearer tokens

## Story workflow

User stories progress through a fixed set of statuses: `Draft → Ready → InProgress → InReview → Done`. Fresh stories start in `Draft`.

Clients advance a story via `PATCH /api/stories/{id}/status` with a JSON body:

```json
{ "status": "InReview", "pullRequestUrl": "https://github.com/org/repo/pull/42" }
```

- The server enforces one transition rule: `Done → Draft` is rejected (use a different target status to re-open). All other transitions are allowed so re-work loops remain possible.
- `pullRequestUrl` is optional; supplying it attaches or replaces the link on the story. Omitting it leaves any existing URL untouched.
- Responses:
  - `200 OK` with the updated story DTO on success.
  - `400 Bad Request` if `status` is not a recognized enum value.
  - `409 Conflict` if the transition is forbidden (currently only `Done → Draft`).
  - `404 Not Found` if the story does not exist or the caller cannot see the owning repository.
- Each successful update emits a `BoardHub` SignalR event so board views refresh live (see Story 3.4).

## Azure DevOps sync

User stories attached to Azure-DevOps-backed repositories can be mirrored to Work Items in the linked AzDO project.

- `POST /api/repositories/{id}/sync-azure-devops` walks every story under the repo and either creates or updates a Work Item (type *User Story*) in the target project. New items have their id persisted in `UserStory.AzureDevOpsWorkItemId`; subsequent pushes update the existing item. Org/project are derived from the repository's clone URL (both `dev.azure.com/{org}/{project}/_git/...` and `{org}.visualstudio.com/{project}/_git/...` are supported). The caller's AzDO linked provider supplies the PAT.
- A hosted `AzureDevOpsBacklogPullJob` polls remote state on a timer configured by `Andy:Issues:AzureDevops:PullIntervalSeconds`. A value ≤ 0 disables the job entirely (the default in test and dev environments). Each tick reads every AzDO-linked repository and calls the sync service's pull path.
- Conflict resolution:
  - **Azure DevOps is authoritative for done/closed state.** When the remote Work Item is in `Closed`, `Done`, or `Removed`, the local story is forced to `Done`. Other remote states (`New`, `Active`, `Resolved`, ...) are ignored on pull so local progress is never rolled back.
  - **Andy Issues is authoritative for title and description.** Pulled snapshots never overwrite local text fields; push is the only direction in which title/description flow.
- Local status → AzDO state mapping used on push: `Draft → New`, `Ready/InProgress → Active`, `InReview → Resolved`, `Done → Closed`.
