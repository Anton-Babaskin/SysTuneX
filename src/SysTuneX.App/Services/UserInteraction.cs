using System.Windows;
using System.Windows.Controls;
using SysTuneX.App.Localization;
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
}

public sealed class UserInteraction : IUserInteraction
{
    private static readonly TimeSpan ToastDuration = TimeSpan.FromSeconds(4);

    private readonly ISnackbarService _snackbar;
    private readonly IContentDialogService _dialogs;
    private readonly ILocalizationService _localization;

    public UserInteraction(
        ISnackbarService snackbar,
        IContentDialogService dialogs,
        ILocalizationService localization)
    {
        _snackbar = snackbar;
        _dialogs = dialogs;
        _localization = localization;
    }

    public void ShowSuccess(string message, string? title = null) =>
        Toast(title ?? string.Empty, message, ControlAppearance.Success, SymbolRegular.CheckmarkCircle24);

    public void ShowInfo(string message, string? title = null) =>
        Toast(title ?? string.Empty, message, ControlAppearance.Info, SymbolRegular.Info24);

    public void ShowWarning(string message, string? title = null) =>
        Toast(title ?? string.Empty, message, ControlAppearance.Caution, SymbolRegular.Warning24);

    public void ShowError(string message, string? title = null) =>
        Toast(title ?? _localization["Msg_Error"], message, ControlAppearance.Danger, SymbolRegular.DismissCircle24);

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

            return result == ContentDialogResult.Primary;
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
