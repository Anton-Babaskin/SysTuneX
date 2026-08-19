using System.Collections.Concurrent;
using System.ComponentModel;
using System.Globalization;
using System.Resources;

namespace SysTuneX.App.Localization;

/// <param name="Code">Empty string means "follow Windows".</param>
public sealed record LanguageOption(string Code, string DisplayName);

public interface ILocalizationService : INotifyPropertyChanged
{
    /// <summary>Indexer form, so XAML can bind to a key and get live updates when the language changes.</summary>
    string this[string key] { get; }

    /// <summary>Looks a key up, returning <paramref name="fallback"/> when the resource is missing.</summary>
    string Get(string key, string? fallback = null);

    /// <summary>Looks a key up and formats it with <paramref name="arguments"/>.</summary>
    string Format(string key, params object?[] arguments);

    CultureInfo CurrentCulture { get; }

    /// <summary>Empty string when the app is following the Windows language.</summary>
    string SelectedLanguageCode { get; }

    IReadOnlyList<LanguageOption> AvailableLanguages { get; }

    void SetLanguage(string languageCode);

    event EventHandler? LanguageChanged;
}

/// <summary>
/// Resource lookup with a live-switchable culture.
///
/// Strings live in .resx so they can be translated with normal tooling, but the accessor is
/// hand-written rather than generated: the generated designer class caches the culture, which
/// is what makes most WPF apps need a restart to change language.
/// </summary>
public sealed class LocalizationService : ILocalizationService
{
    private static readonly ResourceManager Resources =
        new("SysTuneX.App.Resources.Strings", typeof(LocalizationService).Assembly);

    private readonly ConcurrentDictionary<string, string> _cache = new();

    private CultureInfo _culture = CultureInfo.CurrentUICulture;
    private string _selectedLanguageCode = string.Empty;

    /// <summary>
    /// Set once at start-up so the <see cref="LocExtension"/> markup extension, which XAML
    /// creates without going through the container, can reach the same instance.
    /// </summary>
    public static ILocalizationService Instance { get; private set; } = new LocalizationService();

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? LanguageChanged;

    public IReadOnlyList<LanguageOption> AvailableLanguages { get; } =
    [
        new(string.Empty, "System"),
        new("en", "English"),
        new("ru", "Русский"),
    ];

    public CultureInfo CurrentCulture => _culture;

    public string SelectedLanguageCode => _selectedLanguageCode;

    public string this[string key] => Get(key);

    public static void SetInstance(ILocalizationService service) => Instance = service;

    public string Get(string key, string? fallback = null)
    {
        if (string.IsNullOrEmpty(key))
        {
            return fallback ?? string.Empty;
        }

        if (_cache.TryGetValue(key, out string? cached))
        {
            return cached;
        }

        string? value = null;
        try
        {
            value = Resources.GetString(key, _culture);
        }
        catch (MissingManifestResourceException)
        {
            // The satellite assembly for this culture is missing; the neutral fallback covers it.
        }

        // A missing key falls back to the caller's neutral text (the Core catalog carries English),
        // and only shows the raw key when there is nothing else to show.
        value ??= fallback ?? key;
        _cache[key] = value;
        return value;
    }

    public string Format(string key, params object?[] arguments)
    {
        string template = Get(key);
        try
        {
            return string.Format(_culture, template, arguments);
        }
        catch (FormatException)
        {
            return template;
        }
    }

    public void SetLanguage(string languageCode)
    {
        CultureInfo culture;

        if (string.IsNullOrWhiteSpace(languageCode))
        {
            // "Follow Windows" resolves against the OS UI language, not whatever the thread
            // happens to be set to at the moment of the call.
            culture = CultureInfo.InstalledUICulture;
            _selectedLanguageCode = string.Empty;
        }
        else
        {
            try
            {
                culture = CultureInfo.GetCultureInfo(languageCode);
                _selectedLanguageCode = languageCode;
            }
            catch (CultureNotFoundException)
            {
                culture = CultureInfo.InstalledUICulture;
                _selectedLanguageCode = string.Empty;
            }
        }

        if (Equals(culture, _culture) && _cache.Count > 0)
        {
            return;
        }

        _culture = culture;
        _cache.Clear();

        CultureInfo.DefaultThreadCurrentUICulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;

        // "Item[]" is the name WPF listens for to refresh every indexer binding at once.
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }
}
