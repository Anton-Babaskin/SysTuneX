namespace SysTuneX.Core.Models;

public class GameProfile
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string IconGlyph { get; init; } = "\uE7FC";
    public string Description { get; init; } = string.Empty;
    public List<string> TweakIds { get; init; } = [];
    public List<string> ServiceNames { get; init; } = [];
    public Dictionary<string, object> CustomSettings { get; init; } = [];
}
