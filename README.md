# PepeN

PepeN is a Windows-focused continuation built from the PattN codebase. The initial application keeps the upstream/default interface while the project is being renamed and its networking components are reviewed.

## Upstream projects and attribution

- **PattN** by [patterniha](https://github.com/patterniha/PattN) — the direct codebase used as the starting point.
- **v2rayN** by [2dust](https://github.com/2dust/v2rayN) — the upstream project on which PattN is based.
- **Aether** and other bundled components retain their respective upstream licenses and notices.

PepeN is an independent community project and is not affiliated with or endorsed by the upstream maintainers. Existing copyright and license notices are retained in the source tree. See [LICENSE](LICENSE) and the files under [licenses](licenses).

## Build (Windows)

Requirements:
- .NET 10 SDK
- Git with submodule support
- Visual Studio 2022 or newer with the .NET desktop development workload (for the WPF project)

Clone the repository including submodules:

```powershell
git clone --recurse-submodules https://github.com/PeaceOfD/PepeN.git
cd PepeN
dotnet publish .\v2rayN\v2rayN\v2rayN.csproj -c Release -r win-x64 -p:SelfContained=true -p:EnableWindowsTargeting=true
```

The initial Windows WPF application is published from `v2rayN/v2rayN/v2rayN.csproj`. The Avalonia desktop project remains available separately.

## Project status

The repository is being adapted in stages. Branding and build automation are being updated first; tunnel/runtime changes will be integrated only after their Windows build, lifecycle, routing, and cleanup behavior can be validated.
