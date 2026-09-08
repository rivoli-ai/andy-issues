# Your sandboxes

List your environments and remaining capacity with `andy-issues sandboxes mine`.
Close all your environments with `andy-issues sandboxes close-all`; the CLI
asks for confirmation. Automation can use `--force`.

The default capacity is three sandboxes per user. Administrators can change
`andy-issues:sandbox:max-per-user` in Andy Settings; zero disables creation.
Stopped and failed containers count until destroyed. The tenant limit setting
`andy-issues:sandbox:max-per-tenant` is informational and defaults to twenty.
If a bulk close reports failures, retry to close only the remaining environments.
