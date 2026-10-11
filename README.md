# Network Profiles

Windows WPF utility for independent IPv4 profiles on Wi-Fi and Ethernet, designed for PLC commissioning.

## Features

- Network adapter list prioritizes adapters with assigned IPv4 addresses, then link state.
- Adapter-specific profiles: DHCP or static IPv4, prefix, gateway, DNS and notes.
- Input validation and JSON import/export with data validation.
- The application requests administrator privileges at startup; applying profiles does not launch another process or trigger a separate UAC prompt.
- Before applying, a per-adapter configuration snapshot is written to `%LocalAppData%\\NetworkProfiles\\backups`.
- After applying, settings are read back to verify them. On failure, the app attempts to restore the snapshot and reports the backup path.
- System-tray icon. Closing the main window hides it instead of exiting.
- Tray right-click menu provides quick profile selection and apply, plus open, refresh and exit.
- Optional Windows startup; when enabled, the app starts minimized to the tray.
- Opt-in ping scanner for private IPv4 CIDR ranges, limited to at most 1022 host addresses, cancellable and exportable as CSV.
- Profiles and backups stay on the local computer; there is no telemetry.

## Important limitations

- Windows displays UAC when the application starts because it runs as administrator. Profile changes do not trigger another UAC prompt. If apply/verification fails, automatic rollback is attempted but cannot be guaranteed if Windows or the adapter becomes unavailable. Keep the backup path and ensure local access to the PC when changing the active adapter.
- The scanner reports hosts that answer ICMP ping; devices with ping disabled may be omitted, and a response does not identify a host as a PLC.
- The scanner is limited to private IPv4 ranges and `/22` or smaller networks.
- Profile application is for IPv4; IPv6, VLAN, proxy, VPN and remote-device management are out of scope.

## Build, test and publish

Requires Windows and the .NET 10 SDK.

```powershell
dotnet build .\\src\\NetworkProfiles\\NetworkProfiles.csproj -c Release
dotnet test .\\tests\\NetworkProfiles.Tests\\NetworkProfiles.Tests.csproj -c Release
dotnet publish .\\src\\NetworkProfiles\\NetworkProfiles.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o .\\artifacts\\publish
```

GitHub Actions runs the Windows build/tests and publishes a portable single-file `win-x64` artifact. See [Actions](https://github.com/Furkan-x1/network-profiles/actions).

## Security behavior

- The whole application runs elevated after the startup UAC approval, so adapter changes can run in-process without repeated elevation prompts.
- Adapter IDs are checked as GUIDs before querying Windows network configuration.
- Network configuration calls use Windows management APIs, not shell command concatenation.
- Imported profile JSON is data only and is never executed or applied automatically.
- Scanner is opt-in, bounded, and restricted to private IPv4 CIDRs.
