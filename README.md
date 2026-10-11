# Network Profiles

Windows tray utility for switching independent IPv4 profiles on Wi-Fi and Ethernet, designed for PLC commissioning and quick return to DHCP.

## Status

Early starter scaffold; not release-ready. The initial milestone is read-only and does not change Windows network settings.

## Planned features

- Popup window and system-tray integration
- Independent adapter profiles; static IPv4, prefix/mask, gateway, DNS and DHCP
- Snapshot current configuration before changes, verify after applying, attempt rollback on failure
- Validated JSON import/export; importing never applies a profile automatically
- Adapter and connection status, opt-in subnet scan and ping
- Optional launch at sign-in
- Portable self-contained win-x64 publish

## Security design

- Prefer Windows/.NET networking APIs.
- If needed, invoke `%SystemRoot%\\System32\\netsh.exe` by full path with `UseShellExecute=false` and separate argument values. Never build shell commands from profile input.
- Validate IPv4 address, prefix, gateway and DNS before requesting elevation.
- Keep UI unelevated; request UAC only for the narrow network-changing operation.
- Snapshot before mutation; re-read and verify after mutation; attempt restoration if verification fails.
- Treat adapter identifiers and imported JSON as untrusted.
- Scanning is opt-in; no telemetry or network-detail uploads.

## Build

Requires Windows and the .NET SDK with WPF support.

```powershell
dotnet build .\src\NetworkProfiles\NetworkProfiles.csproj -c Release
dotnet test .\tests\NetworkProfiles.Tests\NetworkProfiles.Tests.csproj -c Release
```

Self-contained single-file publish, once implemented and tested:

```powershell
dotnet publish .\src\NetworkProfiles\NetworkProfiles.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

## Milestones

1. Read-only adapter discovery and IPv4 status.
2. Profile model, validation and JSON import/export.
3. Narrowly scoped apply/verify/rollback component.
4. Popup and tray UX.
5. Opt-in scanner, startup behavior, tests and portable publish.

The initial scaffold does not modify network settings.
