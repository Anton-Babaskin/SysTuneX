using Microsoft.Extensions.Logging;
using System.Windows;
using System.Windows.Controls;
using SysTuneX.App.Localization;
using SysTuneX.Core.Models;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Wpf.Ui.Extensions;

namespace SysTuneX.App.Services;

/// <summary>
/// Toasts and confirmations, wrapped so view models can report an outcome without holding a
/// reference to the window. The old build simply swallowed every failure, so a tweak that could
/// not be written looked exactly like one that had been.
/// </summary>
public interface IUserInteraction
{
    void ShowSuccess(string message, string? title = null);

    void ShowInfo(string message, string? title = null);

    void ShowWarning(string message, string? title = null);

    void ShowError(string message, string? title = null);

    /// <summary>A yes/no dialog. Returns false when the dialog is dismissed any other way.</summary>
    Task<bool> ConfirmAsync(string title, string message, string? confirmText = null, CancellationToken cancellationToken = default);

    /// <summary>Confirmation for an advanced change, with the tweak's own warning text included.</summary>
    Task<bool> ConfirmAdvancedAsync(string subject, string description, string warning, CancellationToken cancellationToken = default);

    /// <summary>
    /// Shows exactly what a profile would change - every registry value with its current and
    /// new contents - and asks whether to go ahead.
    /// </summary>
    Task<bool> ConfirmProfileAsync(string profileName, ProfilePreview preview, CancellationToken cancellationToken = default);
}

public sealed class UserInteraction : IUserInteraction
{
    private static readonly TimeSpan ToastDuration = TimeSpan.FromSeconds(4);

    private readonly ISnackbarService _snackbar;
    private readonly IContentDialogService _dialogs;
    private readonly ILocalizationService _localization;
    private readonly ILogger<UserInteraction> _logger;

    public UserInteraction(
        ISnackbarService snackbar,
        IContentDialogService dialogs,
        ILocalizationService localization,
        ILogger<UserInteraction> logger)
    {
        _snackbar = snackbar;
        _dialogs = dialogs;
        _localization = localization;
        _logger = logger;
    }

    // Everything the user is told goes through here, so logging at this one point means the log
    // and the screen can never disagree - which is the whole value of a log during testing.
    public void ShowSuccess(string message, string? title = null)
    {
        _logger.LogInformation("Told the user: {Message}", Combine(title, message));
        Toast(title ?? string.Empty, message, ControlAppearance.Success, SymbolRegular.CheckmarkCircle24);
    }

    public void ShowInfo(string message, string? title = null)
    {
        _logger.LogInformation("Told the user: {Message}", Combine(title, message));
        Toast(title ?? string.Empty, message, ControlAppearance.Info, SymbolRegular.Info24);
    }

    public void ShowWarning(string message, string? title = null)
    {
        _logger.LogWarning("Warned the user: {Message}", Combine(title, message));
        Toast(title ?? string.Empty, message, ControlAppearance.Caution, SymbolRegular.Warning24);
    }

    public void ShowError(string message, string? title = null)
    {
        _logger.LogError("Showed the user an error: {Message}", Combine(title, message));
        Toast(title ?? _localization["Msg_Error"], message, ControlAppearance.Danger, SymbolRegular.DismissCircle24);
    }

    private static string Combine(string? title, string message) =>
        string.IsNullOrWhiteSpace(title) ? message : $"{title} - {message}";

    public async Task<bool> ConfirmAsync(
        string title,
        string message,
        string? confirmText = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            ContentDialogResult result = await _dialogs.ShowSimpleDialogAsync(
                    new SimpleContentDialogCreateOptions
                    {
                        Title = title,
                        Content = message,
                        PrimaryButtonText = confirmText ?? _localization["Common_Continue"],
                        CloseButtonText = _localization["Common_Cancel"],
                    },
                    cancellationToken)
                .ConfigureAwait(true);

            bool confirmed = result == ContentDialogResult.Primary;
            _logger.LogInformation("Asked \"{Title}\" - user {Answer}", title, confirmed ? "confirmed" : "declined");
            return confirmed;
        }
        catch (Exception)
        {
            // No dialog host yet (very early start-up): fail closed rather than acting unasked.
            return false;
        }
    }

    public Task<bool> ConfirmAdvancedAsync(
        string subject,
        string description,
        string warning,
        CancellationToken cancellationToken = default)
    {
        var content = new StackPanel { MaxWidth = 460 };

        content.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = description,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12),
        });

        if (!string.IsNullOrWhiteSpace(warning))
        {
            content.Children.Add(new InfoBar
            {
                Severity = InfoBarSeverity.Warning,
                Title = _localization["Risk_Advanced"],
                Message = warning,
                IsOpen = true,
                IsClosable = false,
            });
        }

        return ShowAsync(subject, content, cancellationToken);
    }

    public Task<bool> ConfirmProfileAsync(
        string profileName,
        ProfilePreview preview,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preview);

        var content = new StackPanel { MaxWidth = 620 };

        content.Children.Add(Caption(string.Format(
            _localization["Preview_Summary"],
            preview.PendingCount,
            preview.PendingServiceCount)));

        // Only what would actually change. Listing a dozen tweaks that are already in place is
        // noise that hides the two that are not.
        int alreadyApplied = preview.Tweaks.Count - preview.PendingCount;
        if (alreadyApplied > 0)
        {
            content.Children.Add(Caption(string.Format(_localization["Preview_AlreadyApplied"], alreadyApplied)));
        }

        var list = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };

        foreach (TweakPreview tweak in preview.Tweaks.Where(t => !t.AlreadyApplied))
        {
            list.Children.Add(Heading($"{tweak.Name}  ·  {RiskName(tweak.Risk)}"));

            if (tweak.IsHandlerDriven)
            {
                // Core parking and the hypervisor flag are not registry writes; saying so beats
                // an empty gap under the heading.
                list.Children.Add(Mono(_localization["Preview_HandlerDriven"]));
                continue;
            }

            foreach (ValueChangePreview value in tweak.Values)
            {
                list.Children.Add(Mono(string.Format(
                    _localization["Preview_ValueLine"],
                    $"{value.KeyPath}\\{value.ValueName}",
                    value.Current ?? _localization["Preview_NotSet"],
                    value.Optimized)));
            }
        }

        foreach (ServicePreview service in preview.Services.Where(s => s.WouldChange))
        {
            list.Children.Add(Heading($"{service.DisplayName}  ·  {RiskName(service.Risk)}"));
            list.Children.Add(Mono(string.Format(
                _localization["Preview_ServiceLine"],
                service.ServiceName,
                service.CurrentStartMode,
                service.TargetStartMode)));
        }

        if (preview.ChangesPowerScheme)
        {
            list.Children.Add(Heading(_localization["Preview_PowerScheme"]));
        }

        content.Children.Add(new ScrollViewer
        {
            Content = list,
            MaxHeight = 320,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        });

        if (preview.RequiresRestart || preview.RequiresSignOut)
        {
            content.Children.Add(new InfoBar
            {
                Severity = InfoBarSeverity.Informational,
                Message = _localization[preview.RequiresRestart ? "Preview_NeedsRestart" : "Preview_NeedsSignOut"],
                IsOpen = true,
                IsClosable = false,
                Margin = new Thickness(0, 12, 0, 0),
            });
        }

        if (preview.HasAdvanced)
        {
            content.Children.Add(new InfoBar
            {
                Severity = InfoBarSeverity.Warning,
                Title = _localization["Risk_Advanced"],
                Message = _localization["Preview_HasAdvanced"],
                IsOpen = true,
                IsClosable = false,
                Margin = new Thickness(0, 8, 0, 0),
            });
        }

        return ShowAsync(profileName, content, cancellationToken);
    }

    private string RiskName(RiskLevel risk) => _localization[$"Risk_{risk}"];

    private static System.Windows.Controls.TextBlock Caption(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 2),
    };

    private static System.Windows.Controls.TextBlock Heading(string text) => new()
    {
        Text = text,
        FontWeight = FontWeights.SemiBold,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 10, 0, 4),
    };

    private static System.Windows.Controls.TextBlock Mono(string text) => new()
    {
        Text = text,
        FontFamily = new System.Windows.Media.FontFamily("Cascadia Mono, Consolas, monospace"),
        FontSize = 12,
        TextWrapping = TextWrapping.Wrap,
        Opacity = 0.85,
        Margin = new Thickness(0, 0, 0, 2),
    };

    private async Task<bool> ShowAsync(string title, object content, CancellationToken cancellationToken)
    {
        try
        {
            ContentDialogResult result = await _dialogs.ShowSimpleDialogAsync(
                    new SimpleContentDialogCreateOptions
                    {
                        Title = title,
                        Content = content,
                        PrimaryButtonText = _localization["Dialog_Advanced_Apply"],
                        CloseButtonText = _localization["Common_Cancel"],
                    },
                    cancellationToken)
                .ConfigureAwait(true);

            return result == ContentDialogResult.Primary;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void Toast(string title, string message, ControlAppearance appearance, SymbolRegular icon)
    {
        void Show()
        {
            try
            {
                _snackbar.Show(title, message, appearance, new SymbolIcon(icon), ToastDuration);
            }
            catch (Exception)
            {
                // The presenter is only wired up once the main window is loaded.
            }
        }

        if (Application.Current?.Dispatcher.CheckAccess() == false)
        {
            Application.Current.Dispatcher.Invoke(Show);
        }
        else
        {
            Show();
        }
    }
}
