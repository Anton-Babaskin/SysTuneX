using System.Management;
using Microsoft.Extensions.Logging;

namespace SysTuneX.Core.Services;

public class GameDetectionService : IGameDetectionService, IDisposable
{
    private static readonly HashSet<string> KnownGames = new(StringComparer.OrdinalIgnoreCase)
    {
        "cs2.exe", "csgo.exe",
        "valorant.exe", "valorant-win64-shipping.exe",
        "fortnite.exe", "fortniteclient-win64-shipping.exe",
        "dota2.exe",
        "overwatch.exe", "overwatch2.exe",
        "r5apex.exe",                        // Apex Legends
        "eldenring.exe",
        "cyberpunk2077.exe",
        "witcher3.exe",
        "gta5.exe",
        "rdr2.exe",
        "destiny2.exe",
        "battlefront2.exe",
        "bf2042.exe",
        "cod.exe", "modernwarfare.exe",
        "leagueoflegends.exe",
        "tslgame.exe",                        // PUBG
        "RainbowSix.exe",
        "DeadByDaylight-Win64-Shipping.exe",
        "starcraft2.exe", "sc2.exe",
        "hearthstone.exe",
        "diablo4.exe",
        "minecraft.exe", "minecraftlauncher.exe",
    };

    private readonly ILogger<GameDetectionService> _logger;
    private ManagementEventWatcher? _startWatcher;
    private ManagementEventWatcher? _stopWatcher;

    public event EventHandler<string>? GameStarted;
    public event EventHandler<string>? GameStopped;

    public GameDetectionService(ILogger<GameDetectionService> logger)
    {
        _logger = logger;
    }

    public void Start()
    {
        try
        {
            _startWatcher = new ManagementEventWatcher(
                new WqlEventQuery("SELECT * FROM Win32_ProcessStartTrace"));
            _startWatcher.EventArrived += OnProcessStart;
            _startWatcher.Start();

            _stopWatcher = new ManagementEventWatcher(
                new WqlEventQuery("SELECT * FROM Win32_ProcessStopTrace"));
            _stopWatcher.EventArrived += OnProcessStop;
            _stopWatcher.Start();

            _logger.LogInformation("Game detection started, monitoring {Count} known games", KnownGames.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Game detection unavailable (WMI error) — likely needs admin");
        }
    }

    public void Stop()
    {
        _startWatcher?.Stop();
        _stopWatcher?.Stop();
    }

    private void OnProcessStart(object sender, EventArrivedEventArgs e)
    {
        var name = e.NewEvent["ProcessName"]?.ToString() ?? "";
        if (KnownGames.Contains(name))
        {
            _logger.LogInformation("Game started: {Name}", name);
            GameStarted?.Invoke(this, name);
        }
    }

    private void OnProcessStop(object sender, EventArrivedEventArgs e)
    {
        var name = e.NewEvent["ProcessName"]?.ToString() ?? "";
        if (KnownGames.Contains(name))
        {
            _logger.LogInformation("Game stopped: {Name}", name);
            GameStopped?.Invoke(this, name);
        }
    }

    public void Dispose()
    {
        _startWatcher?.Dispose();
        _stopWatcher?.Dispose();
    }
}
