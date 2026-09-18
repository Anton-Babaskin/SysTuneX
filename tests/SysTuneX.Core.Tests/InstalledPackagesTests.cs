using SysTuneX.Core.Models;
using Xunit;

namespace SysTuneX.Core.Tests;

/// <summary>
/// What <c>Get-AppxPackage | ConvertTo-Json</c> prints, and what SysTuneX makes of it.
///
/// One detail here is easy to get wrong and impossible to notice while developing: ConvertTo-Json
/// emits a bare object rather than an array when exactly one package matches. A parser that
/// assumed an array works on every developer's machine, where dozens match, and returns nothing on
/// a cleaned-up machine with one - which is precisely the machine whose owner opened this page.
/// </summary>
public sealed class InstalledPackagesTests
{
    [Fact]
    public void A_list_of_packages_is_read()
    {
        IReadOnlyList<InstalledPackage> packages = InstalledPackages.Parse("""
            [
              {"Name":"Microsoft.XboxApp","PackageFamilyName":"Microsoft.XboxApp_8wekyb3d8bbwe","Publisher":"CN=Microsoft"},
              {"Name":"Microsoft.BingWeather","PackageFamilyName":"Microsoft.BingWeather_8wekyb3d8bbwe","Publisher":"CN=Microsoft"}
            ]
            """);

        Assert.Equal(2, packages.Count);
        Assert.Equal("Microsoft.XboxApp", packages[0].Name);
        Assert.Equal("Microsoft.XboxApp_8wekyb3d8bbwe", packages[0].FamilyName);
        Assert.Equal("CN=Microsoft", packages[0].Publisher);
    }

    /// <summary>The trap. One match is an object, not an array of one.</summary>
    [Fact]
    public void A_single_package_comes_back_as_a_bare_object_and_is_still_read()
    {
        IReadOnlyList<InstalledPackage> packages = InstalledPackages.Parse(
            """{"Name":"Microsoft.XboxApp","PackageFamilyName":"Microsoft.XboxApp_8wekyb3d8bbwe","Publisher":"CN=Microsoft"}""");

        Assert.Equal("Microsoft.XboxApp", Assert.Single(packages).Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("[]")]
    [InlineData("Get-AppxPackage : Access is denied")]
    public void Output_with_nothing_usable_in_it_yields_no_packages(string? output)
    {
        Assert.Empty(InstalledPackages.Parse(output));
    }

    /// <summary>
    /// A package with no name cannot be removed - Remove-AppxPackage takes the name - so listing
    /// it would offer the user a button that cannot work.
    /// </summary>
    [Fact]
    public void A_package_with_no_name_is_skipped()
    {
        IReadOnlyList<InstalledPackage> packages = InstalledPackages.Parse("""
            [{"PackageFamilyName":"x_8wekyb3d8bbwe","Publisher":"CN=Microsoft"},
             {"Name":"Microsoft.XboxApp","PackageFamilyName":"y","Publisher":"CN=Microsoft"}]
            """);

        Assert.Equal("Microsoft.XboxApp", Assert.Single(packages).Name);
    }

    /// <summary>
    /// A package Windows described without a family name still has one here: the name. Leaving it
    /// empty would make two publishers' identically named apps indistinguishable.
    /// </summary>
    [Fact]
    public void A_missing_family_name_falls_back_to_the_name()
    {
        IReadOnlyList<InstalledPackage> packages =
            InstalledPackages.Parse("""{"Name":"Microsoft.XboxApp","Publisher":"CN=Microsoft"}""");

        Assert.Equal("Microsoft.XboxApp", Assert.Single(packages).FamilyName);
    }

    /// <summary>
    /// PowerShell can print a property as a number or null when the object did not have it as a
    /// string. That must not throw and must not become the text "null".
    /// </summary>
    [Fact]
    public void A_property_that_is_not_a_string_reads_as_empty_rather_than_throwing()
    {
        IReadOnlyList<InstalledPackage> packages =
            InstalledPackages.Parse("""{"Name":"Microsoft.XboxApp","Publisher":null}""");

        Assert.Equal(string.Empty, Assert.Single(packages).Publisher);
    }

    [Fact]
    public void Malformed_json_yields_no_packages_rather_than_throwing()
    {
        Assert.Empty(InstalledPackages.Parse("""{"Name":"Microsoft.XboxApp" """));
    }
}
