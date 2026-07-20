# SysTuneX

**SysTuneX** is a Windows 10/11 performance and latency optimization application focused on measurable, transparent, and reversible system tuning.

The project provides game-specific optimization profiles, real-time system monitoring, Windows service management, network tuning, privacy controls, system cleanup, and a defined rollback path for supported changes.

> SysTuneX does not rely on undocumented “magic tweaks.” Every optimization should be explainable, auditable, and reversible.

## Features

### Dashboard

* Real-time CPU, memory, and process monitoring
* Optimization Score showing the current system tuning level
* One-click **Quick Optimize**
* One-click **Restore All**
* Standby memory cleanup
* Active optimization status overview

### Game Profiles

SysTuneX provides workload-specific profiles instead of applying the same settings to every system.

* **FPS Competitive** — CS2, Valorant, Apex Legends
* **Battle Royale** — Fortnite, Warzone, PUBG
* **MMO / RPG** — World of Warcraft, Final Fantasy XIV, Baldur’s Gate 3
* **Racing / Simulation** — Forza, iRacing, Assetto Corsa Competizione
* **Maximum Performance** — enables all supported performance-oriented optimizations

Profiles can control process priority, CPU affinity, Windows services, multimedia scheduling, power settings, and other supported system parameters.

### Gaming Tweaks

Supported tuning areas include:

* Xbox Game Bar and Game DVR
* Hardware-Accelerated GPU Scheduling
* GPU and CPU process priority
* Multimedia task scheduling
* Timer resolution
* Mouse acceleration
* Fullscreen optimizations
* Power throttling
* CPU core parking
* Windows visual effects
* Background application activity
* Game-specific process priority and affinity

### Services Management

* View Windows service status
* Start and stop supported services
* Enable or disable individual service optimizations
* Batch service operations
* Risk classification:

  * Safe
  * Moderate
  * Advanced
* Restore previous service states

### Privacy

Privacy controls include:

* Telemetry reduction
* Advertising ID disabling
* Activity History disabling
* Location service controls
* Feedback request reduction
* Microsoft Copilot controls
* Windows Recall controls
* Optional telemetry host blocking through the Windows hosts file

All hosts file changes must be tracked and reversible.

### Network Optimization

* Nagle’s algorithm controls
* TCP acknowledgement frequency tuning
* Windows network throttling controls
* Multimedia network scheduling
* DNS provider switching
* Supported DNS providers:

  * Cloudflare
  * Google
  * Quad9
  * OpenDNS
* Network configuration rollback

Network tweaks are applied only where the selected Windows version and network adapter configuration support them.

### System Cleanup

* User temporary files
* Windows temporary files
* Prefetch data
* Selected application caches
* Optional removal of supported preinstalled applications

Cleanup operations must clearly identify what will be removed before execution.

## Safety Principles

1. Every system change must be documented.
2. Every supported optimization must provide a rollback operation.
3. Destructive or permanent tweaks are out of scope.
4. Default settings must remain conservative.
5. Advanced operations must display an explicit risk level.
6. Existing values must be recorded before they are changed.
7. Performance claims must be supported by reproducible measurements.
8. Unsupported Windows versions or configurations must fail safely.
9. Restore operations must not depend on guessed default values.
10. Administrator privileges must be requested only when required.

## Measurement and Validation

SysTuneX is designed around measurable results rather than placebo optimizations.

Recommended validation metrics include:

* Average FPS
* 1% low FPS
* 0.1% low FPS
* Frametime consistency
* Input latency
* CPU utilization
* Memory utilization
* Background process count
* DPC and ISR latency
* Network latency and packet loss

Results should be compared before and after applying a profile under the same workload and test conditions.

## Technology Stack

| Component            | Technology                                 |
| -------------------- | ------------------------------------------ |
| Runtime              | .NET 8                                     |
| UI                   | WPF with WPF UI                            |
| Architecture         | MVVM                                       |
| MVVM Toolkit         | CommunityToolkit.Mvvm                      |
| Dependency Injection | Microsoft.Extensions.Hosting               |
| System Integration   | Windows APIs, Registry, WMI and PowerShell |
| Native API Access    | P/Invoke                                   |
| Theme                | Dark theme with Mica support               |

## Project Structure

```text
SysTuneX/
├── SysTuneX.sln
├── src/
│   ├── SysTuneX.Core/
│   │   ├── Models/
│   │   ├── Services/
│   │   ├── Tweaks/
│   │   ├── Profiles/
│   │   └── Diagnostics/
│   └── SysTuneX.App/
│       ├── ViewModels/
│       ├── Views/
│       ├── Helpers/
│       ├── Assets/
│       └── Resources/
├── tests/
│   ├── SysTuneX.Core.Tests/
│   └── SysTuneX.IntegrationTests/
├── docs/
└── README.md
```

### `SysTuneX.Core`

Contains the system optimization logic and must not depend on the WPF user interface.

Main responsibilities:

* Registry operations
* Windows service management
* Power plan management
* Network configuration
* Process priority and CPU affinity
* Optimization profiles
* State backup and rollback
* System diagnostics
* Tweak validation

### `SysTuneX.App`

Contains the WPF user interface.

Main responsibilities:

* Dashboard
* Profile selection
* Tweak configuration
* Service management
* Monitoring
* Risk warnings
* Operation progress
* Restore interface

## Requirements

* Windows 10 or Windows 11
* x64 processor
* .NET 8 Runtime, unless using a self-contained build
* Administrator privileges for registry, service, power, network, and system-level operations

Some functionality may depend on the Windows edition, build number, hardware configuration, drivers, or vendor support.

## Build

Restore dependencies:

```powershell
dotnet restore
```

Build the solution:

```powershell
dotnet build
```

Run tests:

```powershell
dotnet test
```

Create a self-contained Windows x64 release:

```powershell
dotnet publish src/SysTuneX.App/SysTuneX.App.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true
```

The published application will be available in the corresponding `bin/Release` publish directory.

## Development Guidelines

* Keep system operations outside the UI layer.
* Use dependency injection for system services.
* Do not modify the registry directly from ViewModels.
* Record the original value before applying a tweak.
* Treat missing registry values separately from existing values.
* Validate Windows version and feature availability.
* Avoid hard-coded assumptions about service state or system defaults.
* Keep PowerShell operations visible and auditable.
* Add automated tests for tweak application and rollback logic.
* Prefer official Windows APIs over undocumented behavior.

## Project Status

SysTuneX is under active development.

Available features, supported Windows versions, and individual optimizations may change while the application architecture and rollback system are being validated.

Features should be considered production-ready only after they have:

1. An implementation
2. Validation for supported Windows versions
3. Automated or reproducible tests
4. A working rollback operation
5. Clear documentation of expected effects and risks

## Disclaimer

SysTuneX changes operating system settings that may affect performance, stability, networking, power consumption, privacy, and application compatibility.

Use the application at your own risk. Review advanced changes before applying them and create a system restore point or backup when testing development builds.

SysTuneX does not guarantee higher FPS, lower latency, or improved performance on every hardware and software configuration.

## License

This project is licensed under the MIT License.
