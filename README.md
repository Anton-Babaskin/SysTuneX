# SysTuneX

*[Русская версия](README.ru.md)*

<div align="center">

<img src="src/SysTuneX.App/Assets/SysTuneX.png" width="110" alt="SysTuneX logo">

# SysTuneX

### Fast Windows tuning. No guesswork. Fully reversible.

Optimize performance, reduce latency and clean up Windows 10/11 with a few clicks.

SysTuneX shows exactly what it changes, saves your original settings and lets you roll everything back.

[Русская версия](README.ru.md) · [Download](https://github.com/Anton-Babaskin/SysTuneX/releases) · [Build from source](#build-from-source) · [Report an issue](https://github.com/Anton-Babaskin/SysTuneX/issues)

<br>

[![Build](https://github.com/Anton-Babaskin/SysTuneX/actions/workflows/build.yml/badge.svg)](https://github.com/Anton-Babaskin/SysTuneX/actions/workflows/build.yml)
[![Release](https://img.shields.io/github/v/release/Anton-Babaskin/SysTuneX?include_prereleases\&sort=semver)](https://github.com/Anton-Babaskin/SysTuneX/releases)
[![Downloads](https://img.shields.io/github/downloads/Anton-Babaskin/SysTuneX/total?label=downloads)](https://github.com/Anton-Babaskin/SysTuneX/releases)
[![License](https://img.shields.io/github/license/Anton-Babaskin/SysTuneX)](LICENSE)

![Windows 10/11](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4?logo=windows11\&logoColor=white)
![.NET 9](https://img.shields.io/badge/.NET-9.0-512BD4?logo=dotnet\&logoColor=white)
![WPF UI](https://img.shields.io/badge/WPF%20UI-4.3-5C2D91)
![MVVM Toolkit](https://img.shields.io/badge/MVVM%20Toolkit-8.4-5C2D91)
![xUnit](https://img.shields.io/badge/xUnit-2.9-512BD4)

</div>

---

## Why SysTuneX?

Windows tuning tools usually fall into one of two categories:

* one-click optimizers that do not tell you what they changed
* giant collections of registry tweaks copied from old gaming guides

SysTuneX takes a different approach.

> **Record before write.**

Before SysTuneX changes a registry value, Windows service, DNS configuration, power scheme or boot option, it records the actual previous state.

Rollback restores **your previous configuration**, not an invented "default".

No undocumented magic tweaks. No silent failures. No pretending that every machine benefits from the same settings.

---

## Quick start

### 1. Download

Go to the:

**[GitHub Releases page](https://github.com/Anton-Babaskin/SysTuneX/releases)**

Download:

```text
SysTuneX.exe
```

SysTuneX is published as a **self-contained win-x64 single-file executable**.

You do not need to install .NET.

### 2. Verify the file

Each release includes `SHA256SUMS.txt`.

```powershell
Get-FileHash .\SysTuneX.exe -Algorithm SHA256
```

Compare the result with the checksum included in the release.

### 3. Run as Administrator

SysTuneX requests elevation automatically because system tuning requires access to the registry, Windows services, networking, power configuration and boot settings.

> The executable is currently not code-signed, so Windows SmartScreen may display a warning.

---

## Features

| Area            | What SysTuneX does                                                                                                       |
| --------------- | ------------------------------------------------------------------------------------------------------------------------ |
| **Dashboard**   | Live CPU and memory monitoring, tuning status, Quick Optimize and full restore                                           |
| **Monitor**     | Frame rate with its 1% low and frame time, CPU and GPU load and temperature, memory — measured without touching the game |
| **Profiles**    | Ready-made tuning profiles for different gaming and workload scenarios                                                   |
| **Gaming**      | Game Bar, Game DVR, fullscreen optimizations, mouse acceleration, CPU scheduling and more                                |
| **Windows 11**  | VBS, HVCI, hypervisor, Recall, Copilot, widgets, search features and other build-aware settings                          |
| **Services**    | Safe service tuning with the original startup configuration recorded before changes                                      |
| **Privacy**     | Telemetry, advertising ID, activity history, suggestions, location, clipboard sync and optional telemetry hosts blocking |
| **Network**     | Nagle tuning, network throttling and DNS latency testing                                                                 |
| **Cleanup**     | Temporary files, update caches, crash dumps, shader caches, thumbnails and other disposable data                         |
| **Game mode**   | One switch that stops background services, raises the power scheme and frees memory — and undoes all of it              |
| **Automation**  | Game mode follows the game, or a schedule, without either one undoing what you switched on by hand                      |
| **Sensors**     | GPU temperature, load and fan through NVML and ADL; CPU temperature from the ACPI thermal zone where firmware exposes one |
| **Search**      | One box across every tweak, service and cleanup target — it navigates to the page and filters it down to the result     |
| **Tray**        | Live counters on hover and the game mode switch in the menu                                                            |
| **Appearance**  | Follows the Windows light/dark setting by default, or pin light or dark by hand; Mica, Acrylic or plain backdrop        |
| **Before/after**| Record the machine either side of a change and see exactly what moved                                                   |
| **Diagnostics** | Persistent logs, verbose logging and a complete diagnostic report                                                        |
| **Change log**  | Full history of recorded changes with individual or complete rollback                                                    |

---

## Tuning profiles

SysTuneX includes workload-specific profiles instead of applying the same configuration to every PC.

| Profile                      | Focus                                                             |
| ---------------------------- | ----------------------------------------------------------------- |
| 🎯 **Competitive FPS**       | Minimum input and background latency                              |
| 🏆 **Battle Royale**         | Network latency and asset-streaming workloads                     |
| 🌍 **Open World & RPG**      | Long sessions, CPU availability and reduced background I/O        |
| 🏎️ **Racing & Simulation**  | Frame-time consistency and latency                                |
| 🎥 **Streaming & Recording** | Gaming performance without starving OBS or capture workloads      |
| 🚀 **Maximum Performance**   | Includes advanced changes for users who understand the trade-offs |

Profiles automatically skip tweaks that do not apply to the current Windows build.

Advanced changes are never silently mixed into normal safe optimization.

---

## Game mode

One switch on the dashboard. It stops the background services the catalog grades as safe, raises the power scheme and frees memory.

It is deliberately **not** "apply a profile under another name". A profile writes registry values that survive a reboot and are undone from the change journal. Game mode only does things that can be undone immediately: services are **stopped, not disabled**, so their start type is untouched and the next boot is exactly as it was, and the previous power scheme is recorded and put back. Nothing it does needs a reboot, so switching it off really restores the machine instead of leaving it half-tuned.

The session is written to disk, so an interrupted one can still be turned off and restored rather than stranding a dozen stopped services.

### It can follow the game

Turn on automatic game mode and SysTuneX watches for a game starting: twenty-five are recognised out of the box, and anything else is a one-field addition by executable name.

### Or the clock

A schedule holds game mode on during a window of the day, optionally on chosen days. It is evaluated against the clock once a minute rather than set as timers on the two edges — a timer misses its moment whenever the machine sleeps through it, and a missed edge would leave game mode stuck on. A window that ends before it starts runs past midnight and belongs to the day it began.

**Neither undoes what you switched on by hand.** Turning game mode on yourself at 23:05 outlives a schedule that ended at 23:00, and a game exiting does not end a session you started.

Automation runs only while SysTuneX is open. Doing it with the app closed would mean a Windows service, and a background service that stops other services is a much bigger thing to ask someone to trust.

---

## Frame rate

SysTuneX counts frames through **Event Tracing for Windows**, reading the Present events the DirectX stack already writes — the same source PresentMon and the Xbox Game Bar counter use.

**Nothing is injected into the game.** RTSS, Afterburner and Fraps hook the graphics API from inside the game process; that is how they draw an overlay, and it is also exactly what anti-cheat looks for. A tool whose whole point is to be run before playing cannot ship something that risks a ban.

The cost of that choice is stated rather than hidden: there is **no overlay** over a fullscreen game. Alt-tab to the Monitor page to read the numbers. The counter deliberately stays pointed at the game when SysTuneX comes to the foreground — retargeting to our own window would show SysTuneX's frame rate, which is worse than useless.

Alongside the average, the Monitor shows the **1% low** — the frame rate of the worst one percent of frames. An average of 144 with a 1% low of 40 stutters, and a steady 90 does not; an average on its own hides the exact problem this tool exists to fix.

Starting a trace session needs administrator rights. Where it cannot start, the page says so instead of showing a blank number.

---

## Temperatures

GPU temperature comes from the vendor's own library, shipped with the driver and needing no install: **NVML** for NVIDIA — temperature, load and fan — and **ADL** for AMD, which reports temperature and fan. CPU temperature comes from the **ACPI thermal zone** where firmware exposes one — that is a board thermal zone rather than the CPU package, and the dashboard says so.

Only ADL's scalar calls are used. Its richer ones take large structs whose layout differs between driver branches, and getting one wrong corrupts memory rather than returning an error — a tuner that can crash the machine it is tuning is worse than one that shows no temperature. Intel GPUs are not covered yet.

**SysTuneX ships no kernel driver, and will not.** Reading a CPU package temperature properly needs a ring-0 helper, and the off-the-shelf ones are on Microsoft's vulnerable driver blocklist and trip anti-cheat. That is not a trade worth making in a tool people install to play games.

So a reading appears only when a sensor actually answered. Where none does, the card says so and why — a missing reading is not zero degrees. Readings outside anything a running part produces are dropped too: some firmware returns a placeholder, and a plausible-looking wrong number is worse than a blank.

---

## Before and after

Record the machine before a change and again after it, then compare the two. The result names every tweak that became applied, every service that started or stopped, and a changed power scheme.

**It is not a performance measurement and does not pretend to be.** It compares configuration, not speed — for frame times, watch the Monitor page while you play, or run the same benchmark on both sides and compare it yourself. Memory and process counts are reported only when they move further than they drift on their own — listing a 3 MB difference as the effect of a tweak would be a lie dressed as data.

---

## Safety by design

SysTuneX is built around reversibility and visibility.

### Record before write

The previous state is journaled before the system is modified.

### Exact rollback

If a registry value existed before SysTuneX changed it, its original value is restored.

If the value did not exist before, rollback removes it instead of inventing one.

### Risk levels

Tweaks are classified by risk.

Advanced changes require explicit confirmation and explain the possible consequences before being applied.

### Windows build awareness

Windows 11-specific settings are only offered where the corresponding feature actually exists.

### Real errors

Failed registry writes, service operations and system commands surface their real error instead of being reported as successful.

### Transparent cleanup

SysTuneX calculates the target paths, file count and total size before cleanup.

---

## Change journal

The rollback journal is stored at:

```text
%ProgramData%\SysTuneX\backup.json
```

It records the state required to restore supported changes, including:

* registry values
* Windows service configuration
* DNS settings
* power configuration
* boot-related options

The Change Log page can restore individual entries or roll back the recorded configuration.

The journal can also be exported as JSON.

---

## Logging & diagnostics

Application logs are stored in:

```text
%ProgramData%\SysTuneX\logs
```

SysTuneX keeps one log file per day and retains logs for seven days.

Messages shown in the UI are logged as well, so the interface and diagnostic log describe the same event.

Enable **Verbose logging** to additionally record registry reads and executed system commands.

The **Build a report** function collects useful troubleshooting information such as:

* Windows build and edition
* hardware information
* elevation state
* recorded changes
* recent application logs

Startup failures are additionally written to:

```text
%ProgramData%\SysTuneX\errors.log
```

---

## Network tools

SysTuneX includes several network-oriented tuning functions.

### DNS latency test

Available providers include:

* Cloudflare
* Google
* Quad9
* OpenDNS
* AdGuard

SysTuneX measures response latency before you choose a resolver.

### Adapter-aware tweaks

Settings such as Nagle's algorithm are applied where Windows actually stores them - per network adapter.

---

## Requirements

* Windows 10 version 1809 / build 17763 or newer
* Windows 11
* x64
* Administrator rights

Availability of individual tweaks may depend on:

* Windows version
* Windows edition
* build number
* hardware
* installed drivers

Unsupported tweaks are filtered instead of being blindly written.

---

## Core stack

| Component            | Technology                         |
| -------------------- | ---------------------------------- |
| Language / Runtime   | C# / .NET 9                        |
| Desktop UI           | WPF                                |
| UI framework         | WPF UI 4.3                         |
| Architecture         | MVVM                               |
| MVVM toolkit         | CommunityToolkit.Mvvm 8.4          |
| Dependency injection | Microsoft.Extensions.Hosting       |
| Windows integration  | Win32 API, Registry, WMI           |
| Frame counting       | Event Tracing for Windows          |
| GPU sensors          | NVML (NVIDIA), ADL (AMD)           |
| System tooling       | powercfg, netsh, bcdedit, ipconfig |
| Testing              | xUnit                              |
| CI/CD                | GitHub Actions                     |

---

## Architecture

SysTuneX separates operating-system logic from the desktop interface.

```text
SysTuneX/
├── src/
│   ├── SysTuneX.Core/
│   │   ├── Abstractions/
│   │   ├── Models/
│   │   ├── Native/
│   │   ├── Services/
│   │   └── Tweaks/
│   │
│   └── SysTuneX.App/
│       ├── Controls/
│       ├── Converters/
│       ├── Localization/
│       ├── Resources/
│       ├── ViewModels/
│       └── Views/
│
├── tests/
│   ├── SysTuneX.Core.Tests/
│   └── SysTuneX.App.Tests/
│
└── .github/workflows/
```

### `SysTuneX.Core`

Contains system operations and tuning logic without a dependency on the WPF UI.

### `SysTuneX.App`

Contains the WPF interface, navigation, resources, localization and view models.

System changes are performed through the Core services instead of directly from the UI layer.

---

## CI

Every push and pull request is validated on a Windows GitHub Actions runner.

The pipeline performs:

```text
Restore
   ↓
Build
   ↓
Core tests
   ↓
WPF startup tests
   ↓
Publish win-x64
   ↓
SHA256 checksum
   ↓
Release artifact
```

The WPF startup suite creates the real application, loads its resources and constructs the main window and pages.

This catches runtime XAML failures that a successful compilation alone cannot detect.

---

### Cutting a release

Releases are published from the default branch only. A release cut from a feature branch would
describe code nobody has merged, and would leave its tag on a commit that may never reach `main`.

1. Add a `## vX.Y.Z` section at the top of [`CHANGELOG.md`](CHANGELOG.md).
2. Put the same version in `release.version`.
3. Merge to `main`.

CI then builds, tests, publishes `SysTuneX.exe` and `SHA256SUMS.txt`, and creates the release
with **only that version's changelog section** plus the shared footer.

A version with a suffix — `v2.5.0-rc1` — is marked as a pre-release; a plain `v2.5.0` is not.
Re-running the job on an unchanged version only refreshes the assets, so it is safe to repeat.

---

## Build from source

Requirements:

* .NET 9 SDK
* Windows for the complete WPF test suite

Clone the repository:

```powershell
git clone https://github.com/Anton-Babaskin/SysTuneX.git
cd SysTuneX
```

Restore and build:

```powershell
dotnet restore
dotnet build
```

Run the Core tests:

```powershell
dotnet test tests/SysTuneX.Core.Tests/SysTuneX.Core.Tests.csproj
```

Run the startup tests. These need a real WPF stack, so they only do anything on Windows:

```powershell
dotnet test tests/SysTuneX.App.Tests/SysTuneX.App.Tests.csproj
```

Publish a self-contained executable:

```powershell
dotnet publish src/SysTuneX.App/SysTuneX.App.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:PublishReadyToRun=true
```

---

## Performance testing

SysTuneX changes system configuration. It does **not** promise a specific FPS increase.

If you want to measure whether a configuration improves your system, compare the same workload before and after using metrics such as:

* average FPS
* 1% and 0.1% lows
* frame-time consistency
* input latency
* DPC / ISR latency
* CPU and memory utilization
* network latency and packet loss

Use the same game scene, graphics settings and test duration for both runs.

---

## Contributing

Issues and pull requests are welcome.

When adding or changing a tweak:

* keep system operations out of the UI layer
* record the original state before modifying it
* distinguish between an absent registry value and a value set to `0`
* validate Windows build requirements
* prefer documented Windows APIs and policies
* add or update catalog tests

---

## Disclaimer

SysTuneX modifies Windows settings related to performance, networking, privacy, services, power management and system behavior.

Although supported changes are recorded for rollback, no tuning tool can guarantee identical results across every Windows installation, hardware configuration or software stack.

Review advanced changes before applying them and consider creating a Windows restore point when testing development builds.

---

## License

SysTuneX is released under the [MIT License](LICENSE).

<div align="center">

**Built for people who want to know what their optimizer actually changed.**

[Download SysTuneX](https://github.com/Anton-Babaskin/SysTuneX/releases) · [Report a bug](https://github.com/Anton-Babaskin/SysTuneX/issues) · [View source](https://github.com/Anton-Babaskin/SysTuneX)

</div>

