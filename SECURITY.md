# Security policy

## Supported versions

The latest release is the supported one. SysTuneX is distributed as a single executable with no
update channel, so a fix means a new release rather than a patch to an old one.

## Reporting a vulnerability

Report privately through GitHub's [private vulnerability
reporting](https://github.com/Anton-Babaskin/SysTuneX/security/advisories/new) rather than in a
public issue.

Please include what an attacker gains, the Windows version and SysTuneX version you saw it on,
and the steps to reproduce it. You will get an acknowledgement within a few days.

## What is in scope

SysTuneX runs elevated and writes to the registry, service configuration and the hosts file, so
the interesting reports are about that surface:

- Anything that lets an unprivileged process influence what SysTuneX writes while elevated
- A path where the change journal cannot restore what was changed, leaving a machine altered with
  no way back
- Tampering with the data directory (`%ProgramData%\SysTuneX`) to make the app act on attacker
  input
- Anything that causes SysTuneX to execute code it did not ship

## What is not

- **That SysTuneX changes system settings.** That is what it is for. Every change is recorded and
  reversible from the change journal, and risky ones are labelled.
- **That it requires administrator rights.** Registry keys under `HKLM`, service configuration
  and ETW sessions cannot be reached without them. The app says so and refuses rather than
  half-working.
- **Antivirus or SmartScreen warnings** on the released executable. It is unsigned; verify the
  checksum published with each release.
- **Reports from a modified build.** Test against a release binary or a clean build of `main`.
