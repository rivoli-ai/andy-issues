-- Copyright (c) Rivoli AI 2026. All rights reserved.
-- Andy Auth seed data for Andy Issues
--
-- This SQL registers the OAuth clients for this service in the Andy Auth database.
-- Run against the andy_auth_dev database (port 5435 by default).
--
-- Usage:
--   psql -h localhost -p 5435 -U postgres -d andy_auth_dev -f config/auth-seed.sql
--
-- Alternatively, add the client registration to andy-auth's DbSeeder.cs.
-- See: ../andy-auth/src/Andy.Auth.Server/Data/DbSeeder.cs

-- =============================================================================
-- 1. Register API resource scope
-- =============================================================================
INSERT INTO "OpenIddictScopes" ("Id", "Name", "DisplayName", "Resources", "ConcurrencyToken")
SELECT
    gen_random_uuid()::text,
    'urn:andy-issues-api',
    'Andy Issues API',
    '["urn:andy-issues-api"]',
    gen_random_uuid()::text
WHERE NOT EXISTS (
    SELECT 1 FROM "OpenIddictScopes" WHERE "Name" = 'urn:andy-issues-api'
);

-- =============================================================================
-- 2. Register confidential API client (service-to-service)
-- =============================================================================
-- Note: ClientSecret must be hashed. For dev, add via DbSeeder.cs instead.
-- The C# seeder handles password hashing automatically.

-- =============================================================================
-- 3. Register public web client (Angular SPA)
-- =============================================================================
-- Note: Public clients have no secret, but redirect URIs must be registered.
-- For dev, add via DbSeeder.cs instead.

-- =============================================================================
-- DbSeeder.cs snippet (add to andy-auth SeedClientsAsync method):
-- =============================================================================
/*
// Andy Issues API Client
var andy_issuesApiClient = await manager.FindByClientIdAsync("andy-issues-api");
if (andy_issuesApiClient != null)
{
    await manager.DeleteAsync(andy_issuesApiClient);
    _logger.LogInformation("Deleted existing OAuth client: andy-issues-api");
}

await manager.CreateAsync(new OpenIddictApplicationDescriptor
{
    ClientId = "andy-issues-api",
    ClientSecret = "andy-issues-secret-change-in-production",
    DisplayName = "Andy Issues API",
    ConsentType = OpenIddictConstants.ConsentTypes.Implicit,
    Permissions =
    {
        OpenIddictConstants.Permissions.Endpoints.Authorization,
        OpenIddictConstants.Permissions.Endpoints.Token,
        OpenIddictConstants.Permissions.Endpoints.Introspection,
        OpenIddictConstants.Permissions.Endpoints.Revocation,

        OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode,
        OpenIddictConstants.Permissions.GrantTypes.RefreshToken,
        OpenIddictConstants.Permissions.GrantTypes.ClientCredentials,

        OpenIddictConstants.Permissions.Scopes.Email,
        OpenIddictConstants.Permissions.Scopes.Profile,
        OpenIddictConstants.Permissions.Scopes.Roles,
        "scp:urn:andy-issues-api",

        OpenIddictConstants.Permissions.ResponseTypes.Code
    },
    RedirectUris =
    {
        new Uri("https://localhost:5400/callback"),
    },
    PostLogoutRedirectUris =
    {
        new Uri("https://localhost:5400/"),
    }
});

_logger.LogInformation("Created OAuth client: andy-issues-api");

// Andy Issues Web Client (Angular SPA)
var andy_issuesWebClient = await manager.FindByClientIdAsync("andy-issues-web");
if (andy_issuesWebClient != null)
{
    await manager.DeleteAsync(andy_issuesWebClient);
    _logger.LogInformation("Deleted existing OAuth client: andy-issues-web");
}

await manager.CreateAsync(new OpenIddictApplicationDescriptor
{
    ClientId = "andy-issues-web",
    DisplayName = "Andy Issues Web",
    ClientType = OpenIddictConstants.ClientTypes.Public,
    ConsentType = OpenIddictConstants.ConsentTypes.Implicit,
    Permissions =
    {
        OpenIddictConstants.Permissions.Endpoints.Authorization,
        OpenIddictConstants.Permissions.Endpoints.Token,

        OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode,
        OpenIddictConstants.Permissions.GrantTypes.RefreshToken,

        OpenIddictConstants.Permissions.Scopes.Email,
        OpenIddictConstants.Permissions.Scopes.Profile,
        OpenIddictConstants.Permissions.Scopes.Roles,
        "scp:urn:andy-issues-api",

        OpenIddictConstants.Permissions.ResponseTypes.Code
    },
    RedirectUris =
    {
        new Uri("https://localhost:4202/callback"),
        new Uri("https://localhost:4200/callback"),
    },
    PostLogoutRedirectUris =
    {
        new Uri("https://localhost:4202/"),
        new Uri("https://localhost:4200/"),
    }
});

_logger.LogInformation("Created OAuth client: andy-issues-web");
*/
