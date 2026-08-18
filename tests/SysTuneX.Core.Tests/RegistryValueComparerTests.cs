using SysTuneX.Core.Models;
using SysTuneX.Core.Services;
using Xunit;

namespace SysTuneX.Core.Tests;

public sealed class RegistryValueComparerTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(0, 0)]
    // A REG_SZ read back as a string has to compare equal to the number the catalog declares,
    // otherwise a tweak like WaitToKillServiceTimeout reads as "not applied" forever.
    [InlineData("2000", 2000)]
    [InlineData(2000, "2000")]
    [InlineData(-1, 4294967295u)]
    public void Numeric_values_compare_by_value_not_by_text(object left, object right) =>
        Assert.True(RegistryValueComparer.AreEqual(left, right));

    [Theory]
    [InlineData(1, 2)]
    [InlineData("High", "Normal")]
    [InlineData(0, "")]
    public void Different_values_are_not_equal(object left, object right) =>
        Assert.False(RegistryValueComparer.AreEqual(left, right));

    [Fact]
    public void Strings_compare_case_insensitively()
    {
        Assert.True(RegistryValueComparer.AreEqual("High", "high"));
        Assert.False(RegistryValueComparer.AreEqual("High", "Higher"));
    }

    [Fact]
    public void Byte_arrays_compare_by_content()
    {
        Assert.True(RegistryValueComparer.AreEqual(new byte[] { 1, 2, 3 }, new byte[] { 1, 2, 3 }));
        Assert.False(RegistryValueComparer.AreEqual(new byte[] { 1, 2, 3 }, new byte[] { 1, 2 }));
    }

    [Fact]
    public void Multi_strings_compare_by_content()
    {
        Assert.True(RegistryValueComparer.AreEqual(new[] { "a", "b" }, new[] { "A", "B" }));
        Assert.False(RegistryValueComparer.AreEqual(new[] { "a" }, new[] { "a", "b" }));
    }

    [Fact]
    public void Null_only_equals_null()
    {
        Assert.True(RegistryValueComparer.AreEqual(null, null));
        Assert.False(RegistryValueComparer.AreEqual(null, 0));
        Assert.False(RegistryValueComparer.AreEqual(0, null));
    }

    [Fact]
    public void Stringify_round_trips_the_types_the_journal_stores()
    {
        Assert.Equal("2000", RegistryValueComparer.Stringify(2000));
        Assert.Equal("High", RegistryValueComparer.Stringify("High"));
        Assert.Equal("010203", RegistryValueComparer.Stringify(new byte[] { 1, 2, 3 }));
        Assert.Equal("a\nb", RegistryValueComparer.Stringify(new[] { "a", "b" }));
    }

    [Fact]
    public void Stringify_is_culture_invariant()
    {
        // A German or Russian locale must not write "1,5" into the journal for 1.5.
        var previous = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("ru-RU");
            Assert.Equal("1.5", RegistryValueComparer.Stringify(1.5));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }
}

public sealed class OperationResultTests
{
    [Fact]
    public void Ok_reports_success_and_a_change()
    {
        OperationResult result = OperationResult.Ok();

        Assert.True(result.Success);
        Assert.True(result.Changed);
    }

    [Fact]
    public void No_change_is_a_success_that_did_nothing()
    {
        OperationResult result = OperationResult.NoChange("already set");

        Assert.True(result.Success);
        Assert.False(result.Changed);
        Assert.Equal("already set", result.Message);
    }

    [Fact]
    public void Fail_carries_the_reason()
    {
        OperationResult result = OperationResult.Fail("access denied");

        Assert.False(result.Success);
        Assert.False(result.Changed);
        Assert.Equal("access denied", result.Message);
    }
}

public sealed class WindowsVersionInfoTests
{
    [Theory]
    [InlineData(22000, true, false)]
    [InlineData(26100, true, false)]
    [InlineData(19045, false, true)]
    [InlineData(17763, false, true)]
    public void Build_number_decides_which_windows_this_is(int build, bool isWindows11, bool isWindows10)
    {
        var version = new WindowsVersionInfo { Major = 10, Minor = 0, Build = build };

        Assert.Equal(isWindows11, version.IsWindows11);
        Assert.Equal(isWindows10, version.IsWindows10);
        Assert.True(version.IsSupported);
    }

    [Fact]
    public void Windows_8_is_not_supported()
    {
        var version = new WindowsVersionInfo { Major = 6, Minor = 3, Build = 9600 };

        Assert.False(version.IsSupported);
    }

    [Fact]
    public void Full_version_includes_the_update_build_revision_when_known()
    {
        Assert.Equal("10.0.26100.2314", new WindowsVersionInfo { Major = 10, Build = 26100, Revision = 2314 }.FullVersion);
        Assert.Equal("10.0.26100", new WindowsVersionInfo { Major = 10, Build = 26100 }.FullVersion);
    }
}
