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

**This is the process-lifecycle foundation, not yet an end-user-ready TUN mode.** The Windows route manager still needs to be implemented and connected before Hev can be enabled for normal traffic. In particular, PepeN must preserve the physical default gateway, add a bypass route for the local SOCKS5 upstream, redirect IPv4/IPv6 default routes to the Hev interface, and restore the original routes on every stop/failure path. Until that work and Windows integration tests are complete, the existing Xray/sing-box TUN implementation remains the active implementation.

The existing ECH/Aether implementation is untouched.
