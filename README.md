# Andy Issues

Issues management service

## Overview

Andy Issues is a microservice in the [Andy ecosystem](https://github.com/rivoli-ai) providing Issues management service.

### Features

- **REST API** - Full CRUD API with Swagger documentation
- **MCP Tools** - AI-assisted management via Model Context Protocol
- **gRPC** - High-performance RPC for service-to-service communication
- **Angular SPA** - Web-based management interface
- **CLI Tool** - Command-line resource management
- **OAuth2/OIDC** - Authentication via Andy Auth
- **RBAC** - Role-based access control via Andy RBAC
- **OpenTelemetry** - Distributed tracing, metrics, and logging

## Quick Start

```bash
# Start infrastructure
docker compose up -d postgres

# Run the API
cd src/Andy.Issues.Api
dotnet run

# Run the client (in a separate terminal)
cd client
npm install && npm start
```

## Architecture

| Layer | Project | Purpose |
|-------|---------|---------|
| Domain | `Andy.Issues.Domain` | Entities, enums |
| Application | `Andy.Issues.Application` | Interfaces, DTOs |
| Infrastructure | `Andy.Issues.Infrastructure` | EF Core, services |
| API | `Andy.Issues.Api` | REST, MCP, gRPC, auth |
| Shared | `Andy.Issues.Shared` | Shared types |
| CLI | `Andy.Issues.Cli` | Command-line tool |

## Documentation

Full documentation available at [rivoli-ai.github.io/andy-issues](https://rivoli-ai.github.io/andy-issues/).

## Ports

| Service | Port |
|---------|------|
| API HTTPS | 5400 |
| API HTTP | 5401 |
| PostgreSQL | 5442 |
| Client (Angular) | 4202 |

## Docker

```bash
# Full stack (PostgreSQL + API)
docker compose up -d

# Embedded mode (SQLite, for Conductor)
docker compose -f docker-compose.embedded.yml up -d
```

## Testing

```bash
# Backend tests
dotnet test

# Frontend tests
cd client && npm test
```

## License

Apache 2.0 - See [LICENSE](LICENSE) for details.

Copyright (c) Rivoli AI 2026
