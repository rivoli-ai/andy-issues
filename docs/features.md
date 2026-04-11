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
