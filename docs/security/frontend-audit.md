# Frontend development dependency exceptions

Review date: 2026-09-08. Owner: andy-issues maintainers. Tracking: [#204](https://github.com/rivoli-ai/andy-issues/issues/204).

The production graph is clean (`npm audit --omit=dev`: zero findings).
The full graph has eight moderate package entries caused by the advisories
below. These entries are accepted **only through 2026-10-08**, only for local
build/test tooling. They must be removed, or explicitly re-reviewed with new
upstream evidence, by that date. They are not approved for production HTTP
serving. The production audit remains a required CI check.

| Package entry | Installed version | Severity | Cause | Exception expires |
| --- | --- | --- | --- | --- |
| `@angular-devkit/build-angular` | 20.3.36 | Moderate | UUID | 2026-10-08 |
| `@angular-devkit/build-webpack` | 0.2003.36 | Moderate | UUID | 2026-10-08 |
| `body-parser` | 1.20.6 | Moderate | QS | 2026-10-08 |
| `express` | 4.22.2 | Moderate | QS | 2026-10-08 |
| `qs` | 6.15.3 | Moderate | QS | 2026-10-08 |
| `sockjs` | 0.3.24 | Moderate | UUID | 2026-10-08 |
| `uuid` | 8.3.2 | Moderate | UUID | 2026-10-08 |
| `webpack-dev-server` | 5.2.6 | Moderate | UUID | 2026-10-08 |

## QS

[Bracket-key comma parsing](https://github.com/advisories/GHSA-x5fp-wj9c-mxmx)
and [attacker-controlled isBuffer denial of service](https://github.com/advisories/GHSA-4mjr-xmp4-gh2g)
affect nested `qs` 6.15.3 beneath Karma/body-parser and webpack-dev-server/Express.
The root dependency already resolves to 6.16.0. The nested consumers pin
`~6.15.1`; 6.16.0 is outside that range. Both `npm audit fix --package-lock-only`
and `npm update qs body-parser express --package-lock-only` retained the same
lockfile. Although audit marks these entries fixable, those compatible
operations did not remove them. Recheck body-parser 1.x and Express 4.x releases
and the enclosing Karma / Angular toolchains at the review deadline.

Exposure is the development/test HTTP listener. Angular dev serving is bound
to `localhost` in angular.json; use it only with trusted local clients, never
as a public preview or production server. Malicious requests to these listeners
can exhaust development resources. The built browser files do not ship these
Node dependencies.

## UUID

[UUID buffer bounds checking](https://github.com/advisories/GHSA-w5hq-g745-h8pq)
affects UUID versions below 11.1.1 and propagates through SockJS,
webpack-dev-server, build-webpack, and build-angular. Audit reports no
compatible fix for this chain. Installed SockJS calls `require('uuid').v4()`
without a supplied buffer (`node_modules/sockjs/lib/transport.js`), whereas the
reported issue concerns v3/v5/v6 buffer handling. This limits the observed
reachability; it does not erase the dependency finding. Recheck a compatible
SockJS/toolchain release by the deadline.

No forced audit fix or transitive override was used. The prior Hono and
development-server advisories were removed by the Angular patch update in
[#209](https://github.com/rivoli-ai/andy-issues/pull/209).
