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
| API HTTPS | 5400 |
| API HTTP | 5401 |
| PostgreSQL | 5442 |
| Client (Angular) | 4202 |

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
