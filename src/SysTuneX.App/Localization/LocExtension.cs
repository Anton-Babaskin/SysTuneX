using System.Windows.Data;
using System.Windows.Markup;

namespace SysTuneX.App.Localization;

/// <summary>
/// XAML shorthand for a localized string: <c>Text="{loc:Loc Dashboard_Title}"</c>.
///
/// It returns a binding rather than a plain string so switching language in Settings updates
/// the whole window immediately instead of at the next start-up.
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class LocExtension : MarkupExtension
{
    public LocExtension()
    {
    }

    public LocExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding($"[{Key}]")
        {
            Source = LocalizationService.Instance,
            Mode = BindingMode.OneWay,
            FallbackValue = Key,
        };

        return binding.ProvideValue(serviceProvider);
    }
}
