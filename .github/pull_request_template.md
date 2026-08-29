## What this changes

<!-- One or two sentences. What is different for someone using SysTuneX after this merges? -->

## Why

<!-- The problem being solved. If it fixes an issue, write "Fixes #123". -->

## How it was verified

<!--
Say what you actually ran, not what you intended to. "Builds" is not verification.

  - [ ] `dotnet build SysTuneX.sln -c Release` — no new warnings
  - [ ] `dotnet test tests/SysTuneX.Core.Tests` — all green
  - [ ] `dotnet test tests/SysTuneX.App.Tests` — Windows only; CI runs it
  - [ ] Ran the app on Windows and used the changed screen

If the change touches a tweak, a service or the registry, say which machine it was
applied and reverted on, and confirm the machine came back to its original state.
-->

## Risk and rollback

<!--
Delete the lines that do not apply.

- Writes to the registry: yes / no. If yes, is the previous value recorded before the write?
- Changes service configuration: yes / no
- Needs a restart or sign-out to take effect: yes / no
- Reversible from the change journal: yes / no. If no, explain why.
-->

## Release

<!--
- [ ] `release.version` bumped, and `CHANGELOG.md` has a section for that exact tag
- [ ] Both `README.md` and `README.ru.md` updated, or neither needed changing
- [ ] Every new user-visible string exists in `Strings.resx` **and** `Strings.ru.resx`

Leave these unchecked and say so if this is not a release-bearing change.
-->
