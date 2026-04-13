# Configuration Seed Files

This directory contains seed data for external Andy ecosystem services that
`andy-issues` depends on.

## Files

| File | Target service | Purpose |
|------|---------------|---------|
| `auth-seed.sql` | Andy Auth | OAuth client registration for andy-issues |
| `rbac-seed.json` | Andy RBAC | Application, roles, and resource types |
| `andy-settings-seed.json` | Andy Settings | Configuration key definitions |

## Applying seeds

### Andy Auth

Run the SQL against the Andy Auth database:

```bash
psql -h localhost -p 5001 -U andy_auth -d andy_auth -f config/auth-seed.sql
```

### Andy RBAC

Use the RBAC admin API or data seeder. See comments in `rbac-seed.json`.

### Andy Settings

Use the andy-settings CLI or admin API:

```bash
andy-settings seed --file config/andy-settings-seed.json
```

Or POST to the andy-settings bulk import endpoint:

```bash
curl -X POST https://localhost:5300/api/settings/seed \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -d @config/andy-settings-seed.json
```

Settings marked `scope: "user"` resolve per-user; `scope: "app"` are
shared across all users of the application.
