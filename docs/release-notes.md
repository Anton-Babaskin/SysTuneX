# SysTuneX 2.0.1

> Notes for the release currently being published. Update this file before tagging a new version;
> if it is missing, the release workflow falls back to auto-generated notes.

**Fixes the crash that stopped 2.0.0 from starting at all.** Do not use 2.0.0 — it fails on every
machine, not just some.

## 2.0.1 — the app now starts

2.0.0 died at launch with:

> Provide value on 'System.Windows.Baml2006.TypeConverterMarkupExtension' threw an exception.

The title bar referenced its icon through `pack://application:,,,/Assets/SysTuneX.png`, but the
.NET SDK has no default rule that compiles a `.png` into the assembly — it globs `**/*.xaml` into
`Page` and nothing else. The file sat in the project as a plain `None` item and never reached the
binary, so `ImageSourceConverter` could not find it and the window's XAML failed to load before a
single pixel was drawn.

The build was green throughout, because XAML is parsed at run time and nothing in the pipeline had
ever run the app.

What changed:

* The icons are declared as `Resource` items, so the pack URI resolves.
* **A startup test suite now runs on the Windows CI runner**: it constructs the real
  `Application`, forces every resource in every merged dictionary to materialise, and builds the
  main window and all ten pages through the container. A build that cannot start can no longer
  reach a release.
* `FilterToggle` moved from `Resources/Theme.xaml` to `App.xaml`. It derives from the WPF UI
  `ToggleButton` style, and a `StaticResource` inside a merged dictionary can only see that
  dictionary's own scope — `Theme.xaml` does not merge the WPF UI controls dictionary.
* The failure dialog is now useful. It leads with the innermost exception, prints the whole chain
  with XAML line and file information, and writes a full report to
  `%ProgramData%\SysTuneX\errors.log`. The 2.0.0 dialog showed only the outer message, which
  named neither the file nor the value.

Everything below applies to the 2.0.0 rewrite and is unchanged in 2.0.1.

## Download

`SysTuneX.exe` below is a self-contained single file. No .NET runtime install needed. It requests
administrator rights on launch, because every change it makes needs a full administrator token.

Verify the download against `SHA256SUMS.txt` if you like:

```powershell
Get-FileHash .\SysTuneX.exe -Algorithm SHA256
```

Windows SmartScreen will warn about an unsigned executable — the binary is not code-signed.
Choose **More info → Run anyway** if you are happy with that.

---

## What changed

This is a full rewrite. The previous build looked complete but did very little on a real machine.

### It could not actually write anything

There was no application manifest, so the process ran without an administrator token while every
HKLM write, service change, `powercfg` call and hosts edit needed one. Failures were caught and
returned as `false`, which the UI displayed as success.

Now: a manifest requesting elevation, per-monitor DPI awareness and a real OS version. Every
operation returns a result carrying the actual Win32 error, and the UI shows it.

### Navigation never worked

Pages took their view model as a constructor argument, but the navigation control was left to
construct them itself, so the window went blank after the first page. The title bar was nested
inside `ContentOverlay`, leaving the window with no drag region.

Now: pages are resolved through the container, and the title bar is a sibling of the content.

### Revert wrote invented values

Each tweak carried a hard-coded "disabled value" that reverting wrote back. A machine that had
hardware GPU scheduling enabled before SysTuneX ran ended up with it disabled.

Now: a journal in `%ProgramData%\SysTuneX\backup.json` records the real previous value — including
"this value did not exist", in which case reverting deletes it — before any change is made.
Services record their original start type; DNS records whether it was DHCP; the boot configuration
records its previous `hypervisorlaunchtype`.

### Several tweaks could not have worked

* Core parking wrote `ValueMax` into a power settings key the power manager does not read. It now
  goes through `powercfg` and re-activates the scheme.
* Mouse acceleration wrote `MouseSpeed` without the two thresholds or the `SystemParametersInfo`
  call, so nothing changed until the next sign-out.
* Ultimate Performance called `setactive` with the well-known GUID, but `duplicatescheme` mints a
  new one. The new GUID is now parsed from the command output.
* Disabling VBS in the registry left the hypervisor still loading from the boot entry.
* Two tweaks shared the id `service_kill_timeout` and two shared `sfio_priority`, so a profile
  referring to either applied whichever was found first. Ids are now verified unique.

### Readings were wrong

Total RAM came from the GC heap limit; GPU VRAM from a UINT32 WMI field that wraps above 4 GB; the
GPU was whichever adapter WMI returned first, usually the integrated one. CPU load came from a
`PerformanceCounter` constructed inside the DI graph, costing a second at start-up and throwing on
machines with a damaged counter registry.

Now: `GlobalMemoryStatusEx`, the display driver key, adapter scoring, and `GetSystemTimes` deltas.

### The UI froze

A timer ran WMI queries and `Process.GetProcesses()` on the UI thread every two seconds, and pages
plus view models were transient, so every navigation created another one and leaked the previous
timer.

## New in this release

* **Russian and English**, switchable at runtime without a restart
* **Light, dark and system theming** — every colour resolves through theme resources, so the app is
  readable in light mode (it previously hard-coded white-on-dark text everywhere)
* **Change log page** listing every recorded value, with per-entry and bulk rollback, and JSON export
* **Windows restore point** before applying a profile
* **Build gating** — Windows 11 features are hidden on Windows 10 rather than written and ignored
* **Explicit confirmation for advanced changes**, naming the real consequence: anti-cheat software,
  BitLocker recovery, Hyper-V and WSL2, printing, notifications
* **Cleanup shows resolved paths, file count and size before deleting anything**, and adds shader
  caches, Delivery Optimization, crash dumps and servicing logs
* **Dashboard** with live CPU and memory sparklines, and a quick-optimise path restricted to tweaks
  marked safe
* 58 tests covering catalog integrity, the backup journal, registry value comparison and translation
  coverage, run in CI on every push

## Known limitations

* Not code-signed, so SmartScreen will warn.
* The hosts-file block can be refused by Microsoft Defender tamper protection. The app reports that
  rather than failing silently.
* Restore points need System Protection switched on for the system drive; if it is off, the app says
  so instead of pretending a restore point was created.
* Ultimate Performance is unavailable on some Windows editions. SysTuneX falls back to High
  Performance and reports which one it used.
