# Network Profiles

[![Build and test](https://github.com/Furkan-x1/network-profiles/actions/workflows/build.yml/badge.svg)](https://github.com/Furkan-x1/network-profiles/actions)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-0078D4?logo=windows)](https://www.microsoft.com/windows)
[![Built with](https://img.shields.io/badge/built%20with-C%23%20%2F%20.NET%2010-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)

**A lightweight Windows utility for managing adapter-specific IPv4 profiles, built for PLC commissioning and network troubleshooting.**

Network Profiles lets you save reusable IPv4 configurations for individual Ethernet and Wi-Fi adapters, switch between DHCP and static settings, and inspect local network hosts without repeatedly entering network details by hand.

## Features

- **Per-adapter profiles** — keep separate settings for each network interface.
- **DHCP and static IPv4** — configure an address, prefix length, gateway, and DNS servers, or return to automatic configuration.
- **Validation before use** — check profile fields and IPv4 values before saving or applying.
- **Apply verification and rollback** — read settings back from Windows after applying a profile; if applying or verification fails, attempt to restore the previous settings.
- **Profile import and export** — move profile definitions using JSON. Imported profiles are validated and are never applied automatically.
- **Adapter overview** — inspect adapter type, link status, IPv4 addresses, and gateways.
- **Profile search and filtering** — find profiles by name or network details, filter by adapter, and group by adapter.
- **System tray** — hide the main window to the tray, quickly apply a saved profile, refresh, or exit.
- **Optional Windows startup** — start minimized to the system tray.
- **Private-network scanner** — optionally ping-scan private IPv4 CIDR ranges and export results to CSV. Scans are limited to 1,022 addresses.
- **Local-first** — profile data stays on the computer; the app has no telemetry.

## Safety notes

- The application requests administrator privileges at startup because changing Windows adapter configuration requires elevation.
- Before applying a profile, the current adapter settings are kept **in memory**. If an operation fails, the app attempts to restore those settings. No backup file is created, and rollback cannot be guaranteed if Windows or the adapter becomes unavailable.
- Applying a profile can interrupt connectivity on the selected adapter. When connecting to a remote machine, ensure you have a recovery path before changing the adapter used for remote access.
- The scanner only checks private IPv4 ranges and is limited to `/22` or narrower networks. It reports hosts that respond to ICMP ping; hosts that block ping may not appear, and a response does not prove that a device is a PLC.
- The app manages IPv4 only. IPv6, VLANs, proxy settings, VPN configuration, and remote PLC programming are outside its scope.

## Download

Open [GitHub Actions](https://github.com/Furkan-x1/network-profiles/actions) and select the latest successful build. Download the `win-x64` portable single-file artifact from the workflow run's **Artifacts** section.

The app is currently built from source by GitHub Actions; check the latest workflow result before using a build.

## Build from source

### Requirements

- Windows 10 or Windows 11
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

### Build and test

Run these commands from the repository root in PowerShell:

```powershell
dotnet restore .\src\NetworkProfiles\NetworkProfiles.csproj
dotnet build .\src\NetworkProfiles\NetworkProfiles.csproj -c Release
dotnet test .\tests\NetworkProfiles.Tests\NetworkProfiles.Tests.csproj -c Release
```

### Publish a portable executable

```powershell
dotnet publish .\src\NetworkProfiles\NetworkProfiles.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o .\artifacts\publish
```

The published executable is written to `artifacts\publish`.

## Data location

Saved profiles are stored locally at:

```text
%LocalAppData%\NetworkProfiles\profiles.json
```

The application does not automatically create a disk backup of current adapter settings. Rollback state exists only in memory for the duration of a profile-apply operation.

## Project details

- **UI:** WPF
- **Language:** C#
- **Target:** .NET 10 for Windows
- **Network configuration:** Windows management APIs
- **Profile format:** JSON
- **Scanner output:** CSV

## License

No license file has been added yet. Until a license is included in the repository, all rights remain with the copyright holder and reuse is not automatically granted.
