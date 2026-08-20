using SysTuneX.Core.Models;

namespace SysTuneX.Core.Tweaks;

/// <summary>
/// Executables SysTuneX watches for out of the box.
///
/// Names only, because that is all the watcher needs and all it should know: no install paths,
/// no launcher integration, nothing that breaks when a game moves between drives. Anything not
/// listed here is a one-field addition by the user.
/// </summary>
public static class GameCatalog
{
    public static IReadOnlyList<WatchedGame> BuiltIn { get; } =
    [
        // Valve
        Game("dota2", "Dota 2"),
        Game("cs2", "Counter-Strike 2"),

        // Competitive shooters
        Game("VALORANT-Win64-Shipping", "VALORANT"),
        Game("r5apex", "Apex Legends"),
        Game("Overwatch", "Overwatch 2"),
        Game("RainbowSix", "Rainbow Six Siege"),

        // Battle royale
        Game("FortniteClient-Win64-Shipping", "Fortnite"),
        Game("cod", "Call of Duty"),
        Game("TslGame", "PUBG"),

        // MOBA and MMO
        Game("League of Legends", "League of Legends"),
        Game("Wow", "World of Warcraft"),
        Game("ffxiv_dx11", "Final Fantasy XIV"),

        // Open world and RPG
        Game("Cyberpunk2077", "Cyberpunk 2077"),
        Game("bg3_dx11", "Baldur's Gate 3"),
        Game("bg3", "Baldur's Gate 3 (Vulkan)"),
        Game("eldenring", "Elden Ring"),
        Game("Witcher3", "The Witcher 3"),
        Game("starfield", "Starfield"),

        // Racing and simulation
        Game("iRacingSim64DX11", "iRacing"),
        Game("acc", "Assetto Corsa Competizione"),
        Game("ForzaHorizon5", "Forza Horizon 5"),
        Game("FlightSimulator", "Microsoft Flight Simulator"),

        // Other
        Game("EscapeFromTarkov", "Escape from Tarkov"),
        Game("RustClient", "Rust"),
        Game("Minecraft.Windows", "Minecraft"),
    ];

    private static WatchedGame Game(string processName, string displayName) =>
        new() { ProcessName = processName, DisplayName = displayName };
}
