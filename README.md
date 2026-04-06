# SysTuneX

Windows 11 Game Optimizer — reduce latency, stabilize frametime, and maximize system performance.

## Features

### Dashboard
- Real-time system monitoring (CPU, RAM, processes)
- Optimization Score showing overall system tune level
- One-click Quick Optimize / Restore All
- RAM Cleaner (standby memory flush)

### Game Profiles
- **FPS Competitive** — CS2, Valorant, Apex (max FPS, min input lag)
- **Battle Royale** — Fortnite, Warzone, PUBG (balanced FPS/quality)
- **MMO / RPG** — WoW, FFXIV, BG3 (stable frametime)
- **Racing / Sim** — Forza, iRacing, ACC (smooth frametime, low wheel lag)
- **Maximum Performance** — all optimizations enabled

### Gaming Tweaks (20+)
- Game Bar / Game DVR disabling
- HAGS (Hardware-Accelerated GPU Scheduling)
- GPU/CPU priority for games
- Timer resolution for 144Hz+ monitors
- Multimedia task prioritization
- Mouse acceleration removal
- Fullscreen optimization control
- Power throttling disable
- Core parking disable
- Visual effects optimization

### Services Management (18 services)
- Toggle individual Windows services
- Color-coded status (Running/Stopped)
- Risk level indicators (Safe/Moderate/Advanced)
- Batch Stop All / Start All

### Privacy (9 tweaks + host blocking)
- Telemetry, Advertising ID, Copilot, Recall blocking
- Activity History, Location, Feedback disabling
- 14 Microsoft telemetry hosts blocked via hosts file

### Network Optimization
- Nagle's algorithm disable
- TCP ACK frequency tuning
- Network throttling removal
- Quick DNS switching (Cloudflare, Google, Quad9, OpenDNS)

### System Cleanup
- Temp files, Windows Temp, Prefetch cleaning
- 25+ bloatware apps removal (Cortana, Teams, Candy Crush, etc.)

## Tech Stack

| Component | Technology |
|---|---|
| Runtime | .NET 8 |
| UI | WPF + WPF UI (Fluent Design) |
| Architecture | MVVM (CommunityToolkit.Mvvm) |
| DI | Microsoft.Extensions.Hosting |
| Theme | Dark + Mica transparency |

## Build

```bash
dotnet restore
dotnet build
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

## Project Structure

```
SysTuneX/
├── SysTuneX.sln
└── src/
    ├── SysTuneX.Core/          # Business logic (zero UI dependencies)
    │   ├── Models/             # TweakItem, ServiceItem, GameProfile, etc.
    │   ├── Services/           # Registry, ServiceManager, Power, Network, etc.
    │   └── Tweaks/             # Gaming, Network, Privacy tweak definitions
    └── SysTuneX.App/           # WPF UI
        ├── ViewModels/         # MVVM ViewModels
        ├── Views/              # XAML Pages
        ├── Helpers/            # Converters
        └── Assets/             # Icons, resources
```

## Requirements

- Windows 10/11
- .NET 8.0 Runtime
- Run as Administrator (for registry/service access)

## License

MIT
