using SysTuneX.App.Services;

namespace SysTuneX.App.ViewModels;

/// <summary>
/// What one section of the settings page needs from the page it sits on.
///
/// The settings page is eight folded cards, and each one used to be a stripe of fields, handlers
/// and summary properties through a single seven-hundred-line class. They are their own view models
/// now, and this is all they share: the stored settings, a way to write them back, and whether the
/// page is still filling its controls in.
/// </summary>
public interface ISettingsHost
{
    AppSettings Settings { get; }

    /// <summary>
    /// True while the page is loading values into its controls.
    ///
    /// Every handler checks it, because assigning a property raises the same change notification a
    /// user's click does - without it, opening the page would write the file eight times and, worse,
    /// would apply each value as though it had just been chosen.
    /// </summary>
    bool IsLoading { get; }

    void Save();
}
