# UI conventions

The Issues web client uses the published `@andy-ui/angular` and `@andy-ui/tokens` 0.1.0 packages for the application shell, sidebar navigation, breadcrumbs, theme control and mutation notifications. Rivoli colors override the shared semantic tokens; existing feature controls use aliases to those tokens.

The theme starts from `andy-issues.theme`, falling back to the system preference. The shared toggle persists the choice. Breadcrumbs follow the active route and retain the repository navigation level. The navigation drawer supports Escape, focus trapping and focus restoration; modified link clicks retain browser behavior.

Successful API mutations and failures use the shared toast service. Forms also retain their contextual validation and errors. The toast host is a polite live region.

The published package does not yet export the dock/noVNC, Markdown/Mermaid or image-lightbox primitives requested in issue #107. Track [Andy UI #2](https://github.com/rivoli-ai/andy-ui/issues/2) for the dock/diagram contract and [Andy UI #36](https://github.com/rivoli-ai/andy-ui/issues/36) for safe Markdown and accessible image viewing. Adopt versioned exports after release. The current sanitized profile preview remains in place; no replacement viewer framework is bundled here.

The 0.1.0 theme-toggle markup and token selector disagree; a scoped selector adapter supplies sizing and visible theme icons until the upstream package aligns them. The production initial bundle is approximately 602 kB, above the existing 500 kB warning threshold and below its unchanged 1 MB error limit. Root Angular bindings currently register the complete core library.
