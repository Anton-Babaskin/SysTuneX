using System.Text.Json;

namespace SysTuneX.Core.Models;

/// <param name="Name">The package name, which is what Remove-AppxPackage takes.</param>
/// <param name="FamilyName">The family name, for telling two publishers' identically named apps apart.</param>
public readonly record struct InstalledPackage(string Name, string FamilyName, string Publisher);

/// <summary>
/// Reads what <c>Get-AppxPackage | ConvertTo-Json</c> printed.
///
/// A separate function because of one detail that is easy to get wrong and impossible to notice:
/// ConvertTo-Json emits a bare object rather than an array when exactly one package matches. A
/// parser that assumed an array would work on every developer's machine, where dozens match, and
/// return nothing on a cleaned-up machine with one - which is precisely the machine whose owner
/// went looking for this page.
/// </summary>
public static class InstalledPackages
{
    public static IReadOnlyList<InstalledPackage> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            IEnumerable<JsonElement> elements = root.ValueKind == JsonValueKind.Array
                ? root.EnumerateArray()
                : [root];

            var packages = new List<InstalledPackage>();

            foreach (JsonElement element in elements)
            {
                if (element.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                string name = Text(element, "Name");
                if (name.Length == 0)
                {
                    continue;
                }

                packages.Add(new InstalledPackage(
                    name,
                    Text(element, "PackageFamilyName") is { Length: > 0 } family ? family : name,
                    Text(element, "Publisher")));
            }

            return packages;
        }
        catch (JsonException)
        {
            // PowerShell printed something that is not JSON - an error, a banner from a profile we
            // asked it to skip. Nothing usable, and guessing would invent packages.
            return [];
        }
    }

    private static string Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
}
