# Network Profiles

Windows WPF utility for managing independent IPv4 profiles on Wi-Fi and Ethernet, designed for PLC commissioning and quick return to DHCP.

## Current status

The app currently:
- Lists network adapters and their IPv4 addresses/gateways; operationally connected adapters appear first.
- Creates, edits, and deletes adapter-specific profile definitions.
- Validates IPv4 address, prefix length, gateway, and DNS input.
- Stores profiles in `%LocalAppData%\NetworkProfiles\profiles.json`.
- Imports and exports JSON profiles. Import validates data and **never applies network settings**.
- Runs automated Windows build and test checks through GitHub Actions.

**Important:** Applying profiles to Windows, UAC elevation, configuration snapshots, verification/rollback, scanner, tray integration, startup, and portable publishing are not implemented yet. Saved profiles do not change the active network configuration.

## Security design

- Prefer Windows/.NET networking APIs.
- If needed, invoke `%SystemRoot%\System32\netsh.exe` by full path with `UseShellExecute=false` and separate argument values. Never build shell commands from profile input.
- Validate IPv4 address, prefix, gateway and DNS before requesting elevation.
- Keep UI unelevated; request UAC only for the narrow network-changing operation.
- Snapshot before mutation; re-read and verify after mutation; attempt restoration if verification fails.
- Treat adapter identifiers and imported JSON as untrusted.
- Scanning will be opt-in; no telemetry or network-detail uploads.

## Build and test

Requires Windows and the .NET 10 SDK with WPF support.

```powershell
dotnet build .\src\NetworkProfiles\NetworkProfiles.csproj -c Release
dotnet test .\tests\NetworkProfiles.Tests\NetworkProfiles.Tests.csproj -c Release
```

Self-contained single-file publish, after the remaining features are implemented and tested:

```powershell
dotnet publish .\src\NetworkProfiles\NetworkProfiles.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

## Roadmap

1. Adapter discovery, status, and connected-first sorting.
2. Profile CRUD, validation, JSON storage and import/export.
3. Safe apply flow: preview, backup, narrow UAC elevation, verify and rollback.
4. Return-to-DHCP action and per-adapter independent configuration.
5. Transaction history and connectivity checks.
6. Opt-in subnet scanner with cancellation and bounded range.
7. System-tray popup and optional startup.
8. Automated tests, recovery testing, and portable single-file release.

The application must not change network settings until the safe apply/rollback milestone is implemented and tested.