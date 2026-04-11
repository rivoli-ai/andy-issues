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
