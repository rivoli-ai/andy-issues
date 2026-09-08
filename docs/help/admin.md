# Users and role membership

Administrators with the user-read permission can open Settings → Users to
search email or display name and filter by application role. Results come from
Andy RBAC and can take up to 30 seconds to reflect membership changes.

Use “Manage in Andy RBAC” to change roles there. Administrators configure that
link with `andy-rbac:admin-url` in Andy Settings. The view is read-only.

CLI: `andy-issues admin users list --query alice --role admin`.
The MCP equivalent is `admin_list_users`. Both require the same permission as
the web view: `andy-issues:admin-users:read`.
