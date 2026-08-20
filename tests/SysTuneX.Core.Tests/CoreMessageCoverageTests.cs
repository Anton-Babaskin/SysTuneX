using System.Xml.Linq;
using SysTuneX.Core.Models;
using Xunit;

namespace SysTuneX.Core.Tests;

/// <summary>
/// The system layer's messages are the ones a user actually reads, and they are the easiest
/// thing in the project to leave half-translated: adding a message is a one-line change in Core
/// that no compiler ties to the resource files. These tests are that tie.
/// </summary>
public sealed class CoreMessageCoverageTests
{
    private const string EnglishResx = "../../../../../src/SysTuneX.App/Resources/Strings.resx";
    private const string RussianResx = "../../../../../src/SysTuneX.App/Resources/Strings.ru.resx";

    public static TheoryData<string> Languages => [EnglishResx, RussianResx];

    [Fact]
    public void Every_template_is_listed_in_All()
    {
        // All is what the other tests walk, so a template missing from it is untested rather
        // than merely untranslated - the worse of the two failures.
        var declared = typeof(CoreMessages)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.FieldType == typeof(MessageTemplate))
            .Select(f => (MessageTemplate)f.GetValue(null)!)
            .ToList();

        Assert.Equal(declared.Count, CoreMessages.All.Count);
        Assert.Empty(declared.Select(t => t.Code).Except(CoreMessages.All.Select(t => t.Code)));
    }

    [Fact]
    public void Codes_are_unique()
    {
        List<string> duplicates = CoreMessages.All
            .GroupBy(t => t.Code, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.Empty(duplicates);
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void Every_message_is_translated(string resx)
    {
        Dictionary<string, string> strings = Load(resx);

        List<string> missing = CoreMessages.All
            .Select(t => $"Core_{t.Code}")
            .Where(key => !strings.ContainsKey(key))
            .ToList();

        Assert.Empty(missing);
    }

    /// <summary>
    /// A translation with the wrong placeholders throws FormatException in front of the user, or
    /// silently drops a value they needed. Arity is the one property worth checking mechanically.
    /// </summary>
    [Theory]
    [MemberData(nameof(Languages))]
    public void Every_translation_takes_the_same_arguments(string resx)
    {
        Dictionary<string, string> strings = Load(resx);
        var wrong = new List<string>();

        foreach (MessageTemplate template in CoreMessages.All)
        {
            if (!strings.TryGetValue($"Core_{template.Code}", out string? translated))
            {
                continue;
            }

            int expected = template.PlaceholderCount;
            int actual = new MessageTemplate(template.Code, translated).PlaceholderCount;

            if (expected != actual)
            {
                wrong.Add($"{template.Code}: expects {expected} argument(s), translation uses {actual}");
            }
        }

        Assert.Empty(wrong);
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void No_translation_is_left_empty(string resx)
    {
        Dictionary<string, string> strings = Load(resx);

        List<string> blank = CoreMessages.All
            .Select(t => $"Core_{t.Code}")
            .Where(key => strings.TryGetValue(key, out string? value) && string.IsNullOrWhiteSpace(value))
            .ToList();

        Assert.Empty(blank);
    }

    [Fact]
    public void Rendering_substitutes_every_argument()
    {
        Assert.Equal(
            @"Access denied writing HKLM\Software\Test\Value. Run SysTuneX as administrator.",
            CoreMessages.RegistryAccessDeniedWrite.Render(@"HKLM\Software\Test", "Value"));

        // A template with no placeholders must survive Render being called with no arguments.
        Assert.Equal(CoreMessages.ProcessZeroAffinity.Format, CoreMessages.ProcessZeroAffinity.Render());
    }

    [Fact]
    public void A_result_built_from_a_template_carries_its_code_and_arguments()
    {
        OperationResult result = OperationResult.Fail(CoreMessages.ServiceStopFailed, "DiagTrack", "Access denied");

        Assert.False(result.Success);
        Assert.Equal("Service_StopFailed", result.Code);
        Assert.Equal(["DiagTrack", "Access denied"], result.Args);

        // The English rendering is still there, because that is what the log records.
        Assert.Equal("Could not stop DiagTrack: Access denied", result.Message);
    }

    private static Dictionary<string, string> Load(string path) =>
        XDocument.Load(path)
            .Root!
            .Elements("data")
            .Where(d => d.Attribute("name") is not null)
            .ToDictionary(
                d => d.Attribute("name")!.Value,
                d => d.Element("value")?.Value ?? string.Empty,
                StringComparer.Ordinal);
}
