# SysTuneX

SysTuneX is a Windows 11 performance and latency optimization project built around measurable, reversible system profiles.

## Project goals

- Apply game-specific optimization profiles
- Manage process priority and CPU affinity
- Reduce avoidable background load during selected workloads
- Provide transparent network, multimedia and service tuning
- Measure changes instead of relying on placebo tweaks
- Restore the previous system state through a defined rollback path

## Planned architecture

- C# and .NET application core
- Windows APIs through P/Invoke
- PowerShell integration for auditable system operations
- Optional Windows service for profile activation
- WPF or WinUI interface
- Optional vendor GPU APIs where a stable integration is available

## Safety principles

1. Every system change must be documented.
2. Every supported change must have a rollback operation.
3. Destructive or permanent tweaks are out of scope.
4. Defaults must remain conservative.
5. Performance claims require reproducible measurements.

## Status

**Design and development.** The repository currently describes the product direction; features listed above should be treated as goals until their implementation and validation are available in the codebase.

Build and installation instructions will be added when the first runnable version is published.
