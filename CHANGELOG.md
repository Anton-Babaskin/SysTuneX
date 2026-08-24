# Changelog

Every released version, newest first. The release workflow publishes only the section
for the version being released, so a release page shows that version and nothing else.

## v2.6.0

Three things this time.

**Applying a profile now shows what it will do first.** Every registry value the profile would
touch, with the machine's current contents beside the new one. Tweaks already in place are
counted rather than listed, because a dozen no-ops hide the two that matter. Two distinctions the
preview keeps: "value does not exist" is not "value is zero" — that difference is why revert can
delete rather than invent — and a tweak handled by code rather than a registry write is marked as
such rather than shown with an empty list.

**One search box for everything.** Roughly a hundred tweaks across four pages, plus services and
cleanup targets; knowing which page a setting lives on is the app's problem, not the user's.
Choosing a result navigates there and filters that page to the item, so it is the one row on
screen. Catalog identifiers are searchable alongside names — someone who knows the value is
called HwSchMode should not have to guess what the tweak is called in their language.

**AMD GPU temperatures**, through ADL, which ships with the driver. Only its scalar calls are
used: the richer ones take large structs whose layout differs between driver branches, and
getting one wrong corrupts memory rather than returning an error. A tuner that can crash the
machine it is tuning is worse than one that shows no temperature. Intel is still not covered.

The vendor libraries cannot be tested without the hardware, so the part that picks between them
now can be: which probe wins, what happens when one throws, and what counts as a believable
number. Readings outside anything a running part produces are dropped — some firmware returns a
placeholder, and a plausible-looking wrong number is worse than a blank tile.

---

## v2.5.0

No new buttons in this one. Game mode was the largest piece of untested code in the project, and
the one whose failure is worst: it does not crash, it quietly leaves a machine with its services
stopped and nothing on screen saying so.

Thirteen tests now cover it, and they pin the properties that matter rather than the happy path:

* Turning game mode off starts back **exactly** what it stopped — a service that was already
  stopped beforehand stays stopped, because "restore" must not leave the machine different.
* Enabling twice does not open a second session. A second one would write an empty
  stopped-services list over the first, and the services it stopped would never come back.
* A session interrupted by the app dying is found on disk by the next launch and can still be
  undone.
* A service that refuses to stop is named in the notes and not recorded as stopped; one that
  refuses to start again is reported, and the session still ends rather than stranding the rest.
* Without administrator rights it refuses instead of half-working.

Seven more cover the automation, including the rule the feature turns on: a session switched on
by hand survives a game exiting, and is not replaced when one starts.

Six more cover the interface's own strings — every key the UI asks for exists, both languages
carry the same keys with the same placeholders, and none is blank or defined twice. Those checks
existed as a script I ran by hand; now they run on every build.

All of it was verified by breaking the code on purpose and watching the right tests fail.

---

## v2.4.0

**A tray icon.** Hover for CPU, memory and whichever temperatures the machine reports; the menu
opens the window, toggles game mode and quits. Optionally, closing the window leaves SysTuneX
running there instead of exiting — gated on the icon actually being visible, so the window can
never vanish with no way back.

**Before and after**, in the change log. Record the machine before a change and again after it,
then compare: the result lists every tweak that became applied, every service that started or
stopped, and a changed power scheme.

It is not a performance measurement and does not pretend to be. SysTuneX cannot see frame times;
for those, run the same benchmark on both sides. Memory and process counts are only reported when
they move further than they drift on their own — listing a 3 MB difference as the effect of a
tweak would be a lie dressed as data.

**A schedule.** Hold game mode on during a window of the day, optionally on chosen days. Checked
once a minute against the clock rather than set as timers on the two edges, because a timer
misses its moment whenever the machine sleeps through it and a missed edge would leave game mode
stuck on. A window that ends before it starts runs past midnight and belongs to the day it began:
Friday 23:00–02:00 is still Friday's window at half past midnight.

Only a session the automation started is ended by it — switching game mode on by hand at 23:05
outlives a schedule that ended at 23:00.

---

## v2.3.0

Turn on **Automatic game mode** in Settings and SysTuneX watches for a game starting: game mode
goes on when it appears and off again when it exits. Twenty-five games are recognised out of the
box — Dota 2, CS2, VALORANT, Apex, Fortnite, WoW, Cyberpunk, iRacing and the rest — and anything
else is a one-field addition by executable name.

Two rules keep it from being annoying:

* **Only an automatic session is undone automatically.** Switching game mode on by hand and
  having it turn itself off because a game exited would be rude, so the session records who
  started it.
* **Detection is edge-triggered.** The watcher fires on the transition, not on every poll —
  otherwise game mode would re-enter every few seconds for as long as the game was up, each time
  recording a fresh session over the previous one's restore data.

It only runs while SysTuneX is open. Doing it with the app closed would mean a Windows service,
and a background service that stops other services is a much bigger thing to ask someone to
trust than a window they can see.

The watch list is matched on process name without the extension, so it survives a game moving
between drives, and typing `dota2.exe` or `dota2` both work.

---

## v2.2.0

The interface was fully translated; the messages underneath it were not. Every failure coming
out of the registry, service, power, network, hosts and restore-point code was an English string
literal at the throw site, so a Russian interface would report a problem in English — which is
what you hit with the System Protection warning.

All 52 of them now live in one catalog with a stable code each. Core still renders English,
because that is the language the log should be in — one the developer reads, rather than whichever
one the machine is set to. The interface looks the code up in its own resources and falls back to
the English text when a translation is missing, so an untranslated message reads a little out of
place instead of showing a raw key.

Three tests hold it together: every message must be translated in every shipped language, every
translation must take the same number of arguments as the original — a mismatch would throw in
front of the user — and no translation may be blank.

---

## v2.1.0

**Game mode** — one switch on the dashboard. It stops the background services the catalog grades
as safe, switches to a high performance power scheme and frees memory. It is deliberately not
"apply a profile": everything behind the switch is undoable *immediately*. Services are stopped,
not disabled, so their start type is untouched and the next boot is exactly as it was; the
previous power scheme is recorded and put back. Nothing needs a reboot, so turning it off really
does restore the machine instead of leaving it half-tuned. The session is written to
`%ProgramData%\SysTuneX\gamemode.json`, so an interrupted session can still be turned off and
restored rather than stranding stopped services.

**Temperatures on the dashboard** — GPU temperature, load and fan through NVIDIA's NVML, which
ships with the driver and needs no install; CPU temperature from the ACPI thermal zone where the
firmware exposes one.

A tile appears only when its sensor actually answered. There is no kernel driver and there will
not be one: reading a CPU package temperature properly needs a ring-0 helper, and every
off-the-shelf one is on Microsoft's vulnerable driver blocklist and trips anti-cheat — not a
trade worth making in a tool aimed at gamers. Where nothing answers, the card says so and why.
AMD and Intel GPUs report no temperature yet; their vendor libraries are not wired up.

**Power plan picker** in Settings, listing the schemes actually registered on the machine rather
than assuming the three well-known GUIDs.


> Notes for the release currently being published. Update this file before tagging a new version;
> if it is missing, the release workflow falls back to auto-generated notes.

---

## v2.0.2

2.0.1 starts and works, but when something misbehaves it leaves nothing behind. Every service in
SysTuneX already logged through `ILogger`; the app only ever registered the debug provider, so on
a real machine all of it went nowhere.

* **A log file.** `%ProgramData%\SysTuneX\logs`, one file per day, kept for seven days, opened
  shared so it can be read and copied while the app runs.
* **Every message you are shown is logged**, at the one place that shows them — so the log and
  the screen cannot disagree about what happened. Confirmations record what you answered.
* **Build a report** in Settings writes one text file with the Windows build and edition, whether
  the process is elevated, the hardware, the full change journal with each recorded previous value,
  and the tail of the log. That single file is what to attach to a bug report.
* **Verbose logging** toggle for every registry read and command line. Applies immediately.


**Fixes the crash that stopped 2.0.0 from starting at all.** Do not use 2.0.0 — it fails on every
machine, not just some.

---

## v2.0.1

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
* Every hand-written pack URI now names its assembly
  (`pack://application:,,,/SysTuneX;component/...`). Without the assembly, WPF resolves the URI
  against `Application.ResourceAssembly` — whichever assembly happens to be the entry point —
  which is correct for the shipped executable but means the same XAML cannot be loaded by
  anything else, a test host included.
* **A startup test suite now runs on the Windows CI runner**: it constructs the real
  `Application`, forces every resource in every merged dictionary to materialise, and builds the
  main window and all ten pages through the container. A build that cannot start can no longer
  reach a release.
* `FilterToggle` moved from `Resources/Theme.xaml` to `App.xaml`. It derives from the WPF UI
  `ToggleButton` style, and a `StaticResource` inside a merged dictionary can only see that
  dictionary's own scope — `Theme.xaml` does not merge the WPF UI controls dictionary. The
  filter bar in `PageParts.xaml` now reaches it with `DynamicResource`, because a
  `StaticResource` inside a `ControlTemplate` resolves against the template's own scope and
  never reaches `Application.Resources` — it would have thrown at first paint.
* The failure dialog is now useful. It leads with the innermost exception, prints the whole chain
  with XAML line and file information, and writes a full report to
  `%ProgramData%\SysTuneX\errors.log`. The 2.0.0 dialog showed only the outer message, which
  named neither the file nor the value.

Everything below applies to the 2.0.0 rewrite and is unchanged in 2.0.1.

---

## v2.0.0

### What changed

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

### New in this release

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

---
