using SysTuneX.Core.Models;
using Xunit;

namespace SysTuneX.Core.Tests;

/// <summary>
/// A hotkey survives a round trip through a settings file people can edit by hand, and refuses the
/// combinations that would make the app a nuisance rather than a tool.
/// </summary>
public sealed class HotkeySpecTests
{
    [Theory]
    [InlineData("Ctrl+Shift+M")]
    [InlineData("ctrl+shift+m")]
    [InlineData("CTRL + SHIFT + M")]
    [InlineData("Shift+Ctrl+M")]     // order is the writer's business, not the reader's
    [InlineData("Control+Shift+M")]
    public void The_same_hotkey_written_in_any_way_reads_the_same(string text)
    {
        Assert.True(HotkeySpec.TryParse(text, out HotkeySpec spec));

        Assert.Equal(HotkeyModifiers.Control | HotkeyModifiers.Shift, spec.Modifiers);
        Assert.Equal("M", spec.Key);
    }

    /// <summary>
    /// What goes into the settings file has to come back out meaning the same thing, or the button
    /// on screen and the key that actually fires drift apart.
    /// </summary>
    [Theory]
    [InlineData("Ctrl+Shift+M")]
    [InlineData("Alt+F8")]
    [InlineData("Ctrl+Alt+Shift+P")]
    [InlineData("Win+G")]
    public void Formatting_and_parsing_are_inverses(string text)
    {
        Assert.True(HotkeySpec.TryParse(text, out HotkeySpec spec));
        Assert.Equal(text, spec.ToString());

        Assert.True(HotkeySpec.TryParse(spec.ToString(), out HotkeySpec again));
        Assert.Equal(spec, again);
    }

    /// <summary>
    /// The rule that matters for whether the app is tolerable to live with: a hotkey with no
    /// modifier fires while the user is typing in any other application on the machine.
    /// </summary>
    [Theory]
    [InlineData("M")]
    [InlineData("F8")]
    public void A_key_with_no_modifier_is_refused(string text)
    {
        Assert.False(HotkeySpec.TryParse(text, out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("Ctrl+Shift")]        // modifiers alone are not a hotkey
    [InlineData("Ctrl+M+P")]          // two real keys is not something Windows can register
    public void Nonsense_is_refused_rather_than_guessed_at(string? text)
    {
        Assert.False(HotkeySpec.TryParse(text, out _));
    }

    /// <summary>
    /// A refusal still hands back the default, so a settings file somebody broke by hand leaves the
    /// app with a working hotkey instead of none.
    /// </summary>
    [Fact]
    public void A_refusal_falls_back_to_the_default()
    {
        Assert.False(HotkeySpec.TryParse("nonsense here", out HotkeySpec spec));
        Assert.Equal(HotkeySpec.Default, spec);
    }

    [Fact]
    public void The_default_is_modified_and_not_a_windows_combination()
    {
        Assert.NotEqual(HotkeyModifiers.None, HotkeySpec.Default.Modifiers);
        Assert.False(HotkeySpec.Default.Modifiers.HasFlag(HotkeyModifiers.Windows));
        Assert.Equal("Ctrl+Shift+M", HotkeySpec.Default.ToString());
    }
}
