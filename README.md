# SysTuneX

*[Русская версия](README.ru.md)*

**SysTuneX** is a Windows 10/11 performance and latency tuner built around one rule: every
change is recorded before it is made, and every recorded change can be put back.

The interface is available in **English and Russian** and follows the Windows light/dark setting.

> No undocumented "magic tweaks". Each optimisation names the registry value it writes, the
> value Windows shipped, and what you give up by applying it.

---

## Download

**[Releases page](https://github.com/Anton-Babaskin/SysTuneX/releases)** — pick the newest version
and download `SysTuneX.exe` from its Assets list.

A single self-contained file; no .NET runtime install needed. It asks for administrator rights on
launch, because every change it makes requires a full administrator token. The binary is not
code-signed, so SmartScreen will warn — **More info → Run anyway** if you are happy with that.

Each release also ships `SHA256SUMS.txt`:

```powershell
Get-FileHash .\SysTuneX.exe -Algorithm SHA256
```

Prefer to build it yourself? See [Build](#build).

---

## What it does

### Dashboard

Live CPU and memory graphs, a tuning score, and the two buttons most people want:

* **Quick optimise** — applies every tweak marked *Safe*, switches to a high performance power
  scheme and trims memory. It never touches anything marked Moderate or Advanced.
* **Restore everything** — writes back every value recorded in the change log.

### Game mode

One switch. It stops the background services the catalog grades as safe, switches to a high
performance power scheme and frees memory — and undoes all of it when switched off. Services are
stopped rather than disabled, so their start type is untouched and the next boot is unchanged;
the previous power scheme is recorded and restored. Nothing it does needs a reboot, which is what
separates it from applying a profile.

### Automatic game mode

SysTuneX can watch for a game starting and switch game mode on and off with it. Twenty-five
games are recognised out of the box and anything else is a one-field addition by executable name.
Only a session the watcher started is ended by the watcher — turning game mode on by hand and
having a game exit undo it would be rude.

It runs only while SysTuneX is open; doing it with the app closed would mean a Windows service.

### Temperatures

GPU temperature, load and fan speed through NVIDIA's NVML, which ships with the driver. CPU
temperature from the ACPI thermal zone where the firmware exposes one — it is a board thermal
zone rather than the CPU package, and the dashboard says so.

SysTuneX ships no kernel driver. Reading a CPU package temperature properly needs a ring-0
helper, and the off-the-shelf ones are on Microsoft's vulnerable driver blocklist and trip
anti-cheat. Where no sensor answers, nothing is shown — a missing reading is not zero degrees.

### Profiles

Preset bundles for different workloads: competitive FPS, battle royale, open world and RPG,
racing and simulation, streaming, and a maximum-performance profile that includes the advanced
changes. Each card shows how much of the profile the machine already has applied.

Advanced changes are opt-in per run, and a Windows restore point can be created first.

### Tweaks

Four pages of individually documented changes — gaming, Windows 11, privacy and network — with
search, risk filters, and per-tweak apply/revert. Each one shows its risk level and whether it
needs a restart, a sign-out, or an Explorer restart.

The Windows 11 page covers what that release added and what it costs: virtualisation-based
security, memory integrity, the boot-level hypervisor, Recall, Click To Do, Copilot, widgets and
search highlights. Everything there is gated on the build number it actually exists in.

### Services

Windows services worth switching off, each with an honest description of what stops working.
Services are set to Manual rather than Disabled wherever something else may legitimately start
them, and the original start type is recorded so restore is exact.

### Privacy

Documented Windows policies for telemetry, the advertising ID, activity history, feedback
prompts, Start menu suggestions, location and clipboard sync — plus an optional hosts-file block
for known telemetry endpoints. The block is a clearly delimited section, the full host list is
shown before anything is written, and only that section is ever removed.

### Network

Nagle's algorithm (applied per adapter, which is where the setting actually lives), the network
throttling index, and a DNS picker with round-trip measurement for Cloudflare, Google, Quad9,
OpenDNS and AdGuard.

### Cleanup

Temporary files, crash dumps, servicing logs, the Windows Update cache, Delivery Optimization,
thumbnails and the DirectX / NVIDIA / AMD shader caches. Every target is measured first and the
exact folders and total size are shown before deletion. Preinstalled Store apps can be removed
individually.

### Diagnostics

SysTuneX writes what it does to `%ProgramData%\SysTuneX\logs`, one file per day, kept for a
week. Every message the app shows you is in there too, so the log and the screen never disagree.
Settings has a **Build a report** button that bundles the machine description, the change journal
and the log tail into a single text file — that one file is what to attach to a bug report.
**Verbose logging** adds every registry read and every command line, and takes effect immediately.

### Before and after

Record the machine before a change and again after it, then compare the two. The result names
every tweak that became applied, every service that started or stopped, and a changed power
scheme. Memory and process counts are reported only when they move further than they drift on
their own.

This is not a performance measurement — SysTuneX cannot see frame times. For those, run the same
benchmark on both sides and compare it yourself.

### Tray icon and schedule

The tray icon shows CPU, memory and temperatures on hover and carries the game mode switch in its
menu. The schedule holds game mode on during a window of the day; it is evaluated against the
clock once a minute rather than set as a timer, so a machine that slept through the start still
catches up.

### Change log

The list of every value SysTuneX recorded before changing it: what it was, who changed it, when,
and whether it is still in effect. Individual entries or the whole set can be rolled back, and
the journal can be exported as JSON.

---

## Safety model

1. **Record before write.** Every registry value, service configuration, power scheme, DNS
   setting and boot flag is journalled before it is touched.
2. **Revert restores the recorded value**, not a guessed default. Where nothing was recorded,
   SysTuneX falls back to the documented Windows value — and where Windows ships without the
   value at all, reverting deletes it rather than inventing one.
3. **Advanced changes require an explicit confirmation** that spells out the consequence
   (anti-cheat, BitLocker recovery, Hyper-V, printing, notifications).
4. **Nothing is silent.** Failures surface with the actual Win32 error instead of being
   swallowed, so "access denied" never looks like "done".
5. **Build gating.** Tweaks that target a Windows 11 feature are hidden on Windows 10 rather
   than written and silently ignored.
6. **Cleanup states what it will delete** — resolved paths, file count and total size — before
   deleting anything.

The journal lives in `%ProgramData%\SysTuneX\backup.json`.

---

## Requirements

* Windows 10 version 1809 (build 17763) or newer, or Windows 11
* x64
* Administrator rights — the app requests them through its manifest, because registry, service,
  power, network and hosts changes all need a full administrator token

Some tweaks depend on the Windows edition, build number, hardware or driver support. Those are
filtered out rather than applied blindly.

---

## Build

```powershell
dotnet restore
dotnet build
dotnet test tests/SysTuneX.Core.Tests/SysTuneX.Core.Tests.csproj
```

Self-contained single-file release:

```powershell
dotnet publish src/SysTuneX.App/SysTuneX.App.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:PublishReadyToRun=true
```

CI builds the same executable on every push and attaches it to the run as an artifact.

`SysTuneX.App.Tests` needs a real WPF stack, so it only does anything on Windows; on any other
host it reports as passed without running. It builds the actual `Application`, forces every
resource in every merged dictionary to materialise, and constructs the main window and all ten
pages through the container — which is the only way to catch a XAML fault, since XAML is parsed
at run time and a green build proves nothing about whether the app starts.

### Cutting a release

Bump `release.version` (for example to `v2.1.0`) and update `docs/release-notes.md`, then
push. CI publishes a GitHub Release at that version with `SysTuneX.exe` and
`SHA256SUMS.txt` attached. Publishing is idempotent: an unchanged version file never
republishes, and re-running the job only refreshes the assets.

---

## Project layout

```text
SysTuneX/
├── src/
│   ├── SysTuneX.Core/            System logic. No UI dependency.
│   │   ├── Abstractions/         Service interfaces
│   │   ├── Models/               Tweak, service, backup and snapshot types
│   │   ├── Native/               P/Invoke and thin wrappers around it
│   │   ├── Services/             Registry, services, power, network, cleanup, backup, engine
│   │   └── Tweaks/               The catalogs: tweaks, services, profiles, cleanup targets
│   └── SysTuneX.App/             WPF UI (MVVM)
│       ├── Controls/             Sparkline
│       ├── Converters/
│       ├── Localization/         Resource lookup and the {loc:Loc} markup extension
│       ├── Resources/            Design tokens, shared templates, en/ru strings
│       ├── ViewModels/
│       └── Views/Pages/
└── tests/
    ├── SysTuneX.Core.Tests/      Catalog, journal and localization coverage
    └── SysTuneX.App.Tests/       Startup smoke tests: real WPF, every page constructed
```

### SysTuneX.Core

Holds all system logic and must not reference the UI. Registry access goes through the 64-bit
view; console tools (`powercfg`, `netsh`, `bcdedit`, `ipconfig`) are run with their output
captured so failures can be reported; service start types are written through the service
control manager rather than `sc.exe`.

The catalog is data: a tweak declares its registry changes, the Windows default for each, the
build range it applies to, and its risk level. Tweaks that are not a plain registry write —
core parking, the hypervisor boot flag, Nagle across adapters — are handled by a registered
handler instead.

### SysTuneX.App

WPF with [WPF UI](https://github.com/lepoco/wpfui) for the Fluent look: Mica backdrop, rounded
corners, and light/dark that follows Windows. Pages are resolved through the container, so a
page can take its view model as a constructor argument, and are cached by the navigation view.

All colours resolve through theme resources, so the whole app works in both light and dark mode.

---

## Technology

| Component            | Technology                                    |
| -------------------- | --------------------------------------------- |
| Runtime              | .NET 9                                        |
| UI                   | WPF + WPF UI 4                                |
| Architecture         | MVVM (CommunityToolkit.Mvvm)                  |
| Dependency injection | Microsoft.Extensions.Hosting                  |
| System integration   | Win32 API, registry, WMI, PowerShell          |
| Tests                | xUnit                                         |

---

## Measuring the result

SysTuneX changes settings; it does not promise frames. If you want to know whether a profile
helped on your machine, compare the same workload before and after:

* average FPS, 1% low and 0.1% low
* frame time consistency
* input latency
* DPC and ISR latency
* CPU and memory utilisation
* network latency and packet loss

Run the same scene, the same settings and the same duration on both sides.

---

## Development notes

* Keep system operations out of the UI layer.
* Never write the registry from a view model — go through `ITweakEngine`.
* Record the original value before changing anything.
* Treat "value absent" as different from "value is zero".
* Validate the Windows build before offering a tweak that needs it.
* Prefer documented Windows APIs and policies over undocumented behaviour.
* Add a catalog test whenever you add a catalog entry.

---

## Disclaimer

SysTuneX changes operating system settings that affect performance, stability, networking,
power consumption, privacy and application compatibility. Every change is recorded and can be
rolled back from the change log, but no tool can guarantee a given result on every machine.

Review advanced changes before applying them, and keep a system restore point when testing
development builds.

---

## License

MIT.
