# Contributing to SysTuneX

SysTuneX changes settings on other people's machines. That single fact decides most of what
follows: the rules below are not style preferences, they are what keeps a tuning tool from
leaving someone worse off than it found them.

## Building

```bash
dotnet restore SysTuneX.sln
dotnet build SysTuneX.sln --configuration Release
dotnet test tests/SysTuneX.Core.Tests/SysTuneX.Core.Tests.csproj
```

`SysTuneX.Core.Tests` runs anywhere with the .NET 9 SDK. `SysTuneX.App.Tests` loads WPF and so
needs Windows; CI runs it on every push, and it is the suite that catches a page whose XAML only
fails when something tries to draw it.

Requirements: the **.NET 9 SDK**, and Windows for the full test suite.

## The rules that are not negotiable

**Record the old value before writing the new one.** Every registry write goes through
`TweakEngine`, which reads and journals the current value first. Doing it the other way round
looks identical in every test that checks the end state, and loses the original on any crash or
access-denied between the two steps — a rollback that cannot roll back. There are tests pinning
this; if you find yourself editing them to make a change pass, stop.

**Revert restores what was recorded, not what Windows ships.** A machine that had a value of 5
gets 5 back, not the documented default. A value recorded as absent is deleted, not replaced with
a number that machine never had.

**No kernel driver.** Reading CPU package temperatures properly needs a ring-0 helper, and the
off-the-shelf ones are on Microsoft's vulnerable-driver blocklist and trip anti-cheat. The same
goes for injecting into game processes to draw an overlay. A tool people install to play games
cannot ship something that risks their account, so features that need either are declined rather
than compromised on. Where that means a number cannot be shown, SysTuneX shows nothing and says
why.

**A missing reading is not zero.** No sensor, no number, and text explaining the absence. A
plausible-looking wrong figure is worse than a blank.

**Every tweak is reversible, or it does not ship.** If a change cannot be undone from the change
journal, it needs a very good reason and a conversation in an issue first.

## Adding a tweak

1. Add it to the catalog under `src/SysTuneX.Core/Tweaks/`, with its registry path, value kind,
   optimized value, risk level and Windows build range.
2. Give it a name and description in **both** `Strings.resx` and `Strings.ru.resx`. A key present
   in one and not the other falls back to English and reads as a bug; there is a test for it.
3. Set the risk level honestly. `Safe` means it cannot break a working system. `Advanced` means
   the user is expected to understand the trade-off, and those are never mixed into normal
   optimization silently.
4. Apply it and revert it on a real machine, and confirm the machine came back.

## Tests

New behaviour needs a test where a test is possible. Where it is not — vendor GPU libraries, ETW
sessions, the service control manager — put the logic that could be wrong behind a seam and test
that, and keep the untestable part as thin as it can be. `FrameTimeWindow` and `IGpuSensorProbe`
are the pattern to copy.

**Verify a test by breaking the code on purpose.** Change the thing the test claims to protect
and watch it fail; a test that passes both ways protects nothing. This is how every safety test in
the project was checked, and it catches assertions that are quietly vacuous.

## Style

Match the file you are editing. A few conventions worth naming:

- Comments explain **why**, not what. If a line needs a comment to say what it does, rename
  something instead. Comments that record a decision — why ADL's scalar calls only, why the frame
  counter stays pointed at the game — are the ones worth writing.
- User-visible strings live in the resx files, never inline.
- Nullable is on. Do not silence it with `!` where a real check belongs.
- No new build warnings. The build runs clean and should stay that way.

## Commits and pull requests

Write the commit message for someone reading it in a year with no memory of the conversation:
a short imperative subject line, then prose explaining why the change is right, not a restatement
of the diff. Say what you rejected and why, when that is the interesting part.

Fill in the pull request template honestly, especially the verification section. "Builds" is not
verification.

## Releases

See [RELEASING.md](RELEASING.md). Every merge to `main` that changes behaviour gets its own
version and its own section in `CHANGELOG.md`.
