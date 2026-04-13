# Deployment

## Docker Compose (Development)

```bash
# Full stack with PostgreSQL
docker compose up -d

# Embedded mode with SQLite (for Conductor)
docker compose -f docker-compose.embedded.yml up -d
```

## Docker Build

```bash
docker build -t andy-issues:latest .
```

## Kubernetes

### Prerequisites
- Kubernetes cluster
- `kubectl` configured
- Container registry access

### Deployment Steps

1. Build and push image:
```bash
docker build -t registry.example.com/andy-issues:latest .
docker push registry.example.com/andy-issues:latest
```

2. Create namespace and secrets:
```bash
kubectl create namespace andy-issues
kubectl create secret generic andy-issues-db \
  --from-literal=connection-string="Host=postgres;Port=5432;Database=andy_issues;Username=andy_issues;Password=CHANGE_ME"
```

3. Apply manifests (create your own or use Helm).

## Environment Variables

| Variable | Description | Default |
|----------|-------------|---------|
| `ASPNETCORE_ENVIRONMENT` | Runtime environment | `Production` |
| `ASPNETCORE_URLS` | Listen URLs | `https://+:8443;http://+:8080` |
| `ConnectionStrings__DefaultConnection` | Database connection string | (see appsettings) |
| `Database__Provider` | `PostgreSql` or `Sqlite` | `PostgreSql` |
| `AndyAuth__Authority` | Andy Auth server URL | `https://localhost:5001` |
| `AndyAuth__Audience` | JWT audience | `urn:andy-issues-api` |
| `Rbac__ApiBaseUrl` | Andy RBAC server URL | `https://localhost:5003` |
| `Rbac__ApplicationCode` | RBAC application code | `andy-issues` |
| `OpenTelemetry__OtlpEndpoint` | OTLP collector endpoint | (empty) |

## Ports

| Service | Port |
|---------|------|
| API HTTPS | 5410 |
| API HTTP | 5411 |
| PostgreSQL | 5443 |
| Client (Angular) | 4203 |

## Conductor Integration

To embed this service in Conductor, use the SQLite configuration:

```bash
docker compose -f docker-compose.embedded.yml up -d
```

Or configure the API directly:
```bash
export Database__Provider=Sqlite
export ConnectionStrings__DefaultConnection="Data Source=andy_issues.db"
dotnet run --project src/Andy.Issues.Api
```

### Conductor Environment Overrides

When embedded in Conductor, these environment variables are injected by
`IssuesServiceConfig.swift`:

| Variable | Source | Default | Purpose |
|----------|--------|---------|---------|
| `Database__Provider` | Conductor | `Sqlite` | Use SQLite for embedded mode |
| `ConnectionStrings__DefaultConnection` | Conductor | `Data Source=<conductor-data>/andy_issues.db` | SQLite DB path |
| `AndyAuth__Authority` | Conductor | `https://localhost:<auth-port>` | Andy Auth IdP URL |
| `AndyAuth__Audience` | Conductor | `urn:andy-issues-api` | JWT audience |
| `AndySettings__ApiBaseUrl` | Conductor | `https://localhost:<settings-port>` | Andy Settings URL |
| `AndyContainers__BaseUrl` | Conductor | `https://localhost:<containers-port>` | andy-containers URL |
| `AndyCodeIndex__BaseUrl` | Conductor | `https://localhost:<code-index-port>` | andy-code-index URL |
| `Rbac__ApiBaseUrl` | Conductor | `https://localhost:<rbac-port>` | Andy RBAC URL |
| `Rbac__ApplicationCode` | Conductor | `andy-issues` | RBAC app code |
| `OpenTelemetry__OtlpEndpoint` | Conductor | `http://localhost:4317` | OTLP collector |
| `ASPNETCORE_ENVIRONMENT` | Conductor | `Production` | Runtime environment |
| `ASPNETCORE_URLS` | Conductor | `https://+:<assigned-port>` | Listen URL |

## Database schema

Schema evolution uses two complementary paths:

- **PostgreSQL (default)** — EF Core migrations committed under
  `src/Andy.Issues.Infrastructure/Data/Migrations/`. In Development the API
  calls `Database.MigrateAsync()` on startup; in other environments run:

  ```bash
  dotnet ef database update \
    --project src/Andy.Issues.Infrastructure \
    --startup-project src/Andy.Issues.Infrastructure
  ```

- **SQLite (Conductor / embedded)** — the API calls
  `Database.EnsureCreatedAsync()` on startup, which materializes the current
  schema without a migrations history. This keeps the embedded path
  self-contained and migration-free.

To add a new migration:

```bash
dotnet ef migrations add <Name> \
  --project src/Andy.Issues.Infrastructure \
  --startup-project src/Andy.Issues.Infrastructure \
  --output-dir Data/Migrations
```
