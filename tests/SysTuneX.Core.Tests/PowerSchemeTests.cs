using SysTuneX.Core.Abstractions;
using Xunit;

namespace SysTuneX.Core.Tests;

/// <summary>
/// Whether a scheme counts as high performance, which is a fifth of the tuning score.
///
/// The GUID half is exact. The name half exists for the copy SysTuneX makes itself when Windows
/// ships with neither built-in scheme - <c>powercfg /duplicatescheme</c> mints a new GUID and keeps
/// the source scheme's name - and it is the half that needs a test standing over it, because it is
/// string matching against text Windows wrote in the user's language.
/// </summary>
public sealed class PowerSchemeTests
{
    [Theory]
    [InlineData("Ultimate Performance")]
    [InlineData("High performance")]
    [InlineData("High Performance")]                    // Windows has capitalised it both ways
    [InlineData("Ultimate Performance - SysTuneX")]     // a duplicate, renamed
    public void A_scheme_named_for_performance_counts(string name)
    {
        Assert.True(new PowerScheme(Guid.NewGuid(), name, IsActive: true).IsHighPerformance);
    }

    [Theory]
    [InlineData("Balanced")]
    [InlineData("Power saver")]
    [InlineData("Сбалансированная")]
    public void Any_other_scheme_does_not(string name)
    {
        Assert.False(new PowerScheme(Guid.NewGuid(), name, IsActive: true).IsHighPerformance);
    }

    /// <summary>The two built-in schemes keep their GUIDs whatever Windows calls them.</summary>
    [Fact]
    public void The_built_in_schemes_are_recognised_by_guid_whatever_their_name()
    {
        Assert.True(new PowerScheme(PowerScheme.UltimatePerformance, "Максимальная производительность", true)
            .IsHighPerformance);

        Assert.True(new PowerScheme(PowerScheme.HighPerformance, "Высокая производительность", true)
            .IsHighPerformance);

        Assert.False(new PowerScheme(PowerScheme.Balanced, "Сбалансированная", true).IsHighPerformance);
    }

    /// <summary>
    /// The case this cannot answer, and the reason <c>IsHighPerformanceActiveAsync</c> exists.
    ///
    /// A duplicate SysTuneX made on a Russian Windows carries a fresh GUID and the Russian name, so
    /// neither half of this property matches. Only the service knows which copy it made. Left to
    /// this property alone the dashboard would report a tuned machine as untuned and quietly
    /// withhold a fifth of the score - which is what it did.
    /// </summary>
    [Fact]
    public void A_duplicate_on_a_translated_windows_is_not_recognisable_from_the_scheme_alone()
    {
        Assert.False(
            new PowerScheme(Guid.NewGuid(), "Высокая производительность", IsActive: true).IsHighPerformance);
    }
}
