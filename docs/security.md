# Security

## Authentication

### Andy Auth (OAuth2/OIDC)

This service integrates with [Andy Auth](https://github.com/rivoli-ai/andy-auth) for authentication:

- **Protocol**: OAuth 2.0 Authorization Code with PKCE
- **Token format**: JWT Bearer tokens
- **Authority**: Configured via `AndyAuth:Authority`
- **Audience**: `urn:andy-issues-api`

### OAuth Client Registration

Two OAuth clients are registered in Andy Auth:

1. **`andy-issues-api`** (Confidential) - For service-to-service communication
2. **`andy-issues-web`** (Public) - For the Angular SPA

See `config/auth-seed.sql` for the seed data.

### Test User

- **Email**: `test@andy.local`
- **Password**: `Test123!`
- **Role**: User (with super-admin in RBAC)

## Authorization

### Andy RBAC

Role-based access control is provided by [Andy RBAC](https://github.com/rivoli-ai/andy-rbac):

- **Application code**: `andy-issues`
- **Roles**: admin, user, viewer
- **Actions**: read, write, delete, admin

See `config/rbac-seed.json` for the RBAC configuration.

## Transport Security

- **HTTPS everywhere**: TLS is enforced from development to production
- **Self-signed certs**: Generated automatically in Docker for development
- **Corporate CAs**: Supported via the `certs/` directory
- **Certificate injection**: At build time and runtime in Docker

## Sandbox certificate trust

Sandboxes are containers managed by [`andy-containers`](https://github.com/rivoli-ai/andy-containers); andy-issues never mounts volumes or builds images itself. Trust-store provisioning for corporate CAs is therefore owned by the andy-containers side of the integration, not by andy-issues.

**Developer flow**

1. Drop your corporate CA bundle (`.crt` files, PEM-encoded) into `andy-containers/certs/` *before* building the sandbox template image. This repo's `certs/` directory is for the andy-issues service itself and is not copied into sandboxes.
2. Rebuild the `devpilot-desktop` template in andy-containers. The template's Dockerfile copies `/certs/` into the image and runs `update-ca-certificates` so the system trust store picks them up.
3. Any sandbox created from that template inherits the corporate trust store automatically — nothing on the andy-issues side needs to change and no env vars or mounts are passed through the sandbox create call for this.

**Verification**

From inside a running sandbox (e.g. via `POST /api/sandboxes` then exec'ing a shell through andy-containers):

```bash
update-ca-certificates --verbose
# Expected: the corporate certs show up in /etc/ssl/certs/ca-certificates.crt
openssl s_client -connect internal.example.com:443 -showcerts
# Expected: verification succeeds without -servername hints or -CAfile overrides
```

**Boundary note**

If a corporate cert change needs to reach existing running sandboxes, destroy and recreate them — certs are baked in at image build time and are not hot-reloaded. When that friction becomes painful, the fix belongs on the andy-containers side (e.g. a runtime mount) rather than in andy-issues: see the container management memo and Story 4.3 for the cross-repo direction.

## API Security

- **Swagger**: Bearer authentication scheme configured
- **MCP**: Requires authorization
- **gRPC**: Uses the same Bearer authentication
- **Health endpoint**: Unauthenticated (for load balancer probes)

## Best Practices

- Never commit secrets to the repository
- Use environment variables for sensitive configuration
- Rotate tokens and passwords regularly
- Review RBAC permissions periodically
