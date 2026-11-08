# Hev Windows integration

PepeN vendors the Windows runtime from the upstream `hev-socks5-tunnel` project.

- Upstream: https://github.com/heiher/hev-socks5-tunnel
- Pinned runtime: `2.19.3`
- Upstream license: MIT (see `licenses/hev-socks5-tunnel-MIT.txt`)

## Runtime files

The Windows x64 build pipeline downloads the upstream `hev-socks5-tunnel-win64.zip` release asset and places these files beside the application:

- `hev-socks5-tunnel.exe`
- `wintun.dll`

The DLL must match the executable architecture. Hev requires an elevated process on Windows because it creates and configures a Wintun interface.

## Integration status

The `HevTunService` class currently implements validated configuration generation, executable/DLL checks, process startup, output forwarding, and process/config cleanup. Startup is reported as successful only after Windows exposes the `PepeNTun` adapter in the `Up` state; a process that exits early or fails to create the adapter is treated as a startup failure.

**This is the process-lifecycle foundation, not yet an end-user-ready TUN mode.** The side-effect-free `HevRoutePlan` validates and orders route additions: explicit bypass routes to every resolved upstream IP first, followed by IPv4 and (when requested) IPv6 defaults through Hev. `WindowsHevRouteBackend` can inspect physical default routes and add/remove exact routes through PowerShell; route mutations require an elevated process. `HevRouteTransaction` writes an atomic recovery journal before mutations and retains it for the whole tunnel lifetime, removing only routes recorded by that transaction during explicit stop or crash recovery. If a route operation fails, it attempts rollback and retains the journal if rollback cannot complete.

This is still not wired into the running core lifecycle. `HevUpstreamEndpointResolver` now extracts known remote endpoint fields from generated Xray and sing-box outbound arrays and resolves hostnames before route planning. It deliberately ignores SNI, HTTP Host, DNS rules and other non-dial fields, and fails closed if it cannot resolve every discovered endpoint. Unsupported/custom outbound schemas still need an explicit adapter. Before enabling Hev for normal traffic, PepeN must verify and safely manage the Hev interface metric (including restoring its previous value), integrate startup/stop/unexpected-exit/startup-crash recovery into the core lifecycle, and test on real Windows machines. No route changes are made automatically by this PR. Until that work and Windows integration tests are complete, the existing Xray/sing-box TUN implementation remains the active implementation.

The existing ECH/Aether implementation is untouched.
