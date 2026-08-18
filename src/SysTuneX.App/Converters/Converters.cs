using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using SysTuneX.Core.Models;

namespace SysTuneX.App.Converters;

/// <summary>Base class that makes the one-way converters below a little less repetitive.</summary>
public abstract class OneWayConverter : IValueConverter
{
    public abstract object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException($"{GetType().Name} is one-way only.");
}

public sealed class BoolToVisibilityConverter : OneWayConverter
{
    public override object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;
}

public sealed class InverseBoolToVisibilityConverter : OneWayConverter
{
    public override object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;
}

public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
}

/// <summary>Shows an element only when a collection or count is empty, for "nothing found" states.</summary>
public sealed class EmptyToVisibilityConverter : OneWayConverter
{
    public override object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        int count = value switch
        {
            null => 0,
            int number => number,
            string text => text.Length,
            System.Collections.ICollection collection => collection.Count,
            System.Collections.IEnumerable enumerable => enumerable.Cast<object>().Count(),
            _ => 1,
        };

        bool invert = string.Equals(parameter as string, "invert", StringComparison.OrdinalIgnoreCase);
        bool visible = invert ? count > 0 : count == 0;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }
}

public sealed class NullToVisibilityConverter : OneWayConverter
{
    public override object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool invert = string.Equals(parameter as string, "invert", StringComparison.OrdinalIgnoreCase);
        bool hasValue = value is not null && value is not string { Length: 0 };
        return (invert ? !hasValue : hasValue) ? Visibility.Visible : Visibility.Collapsed;
    }
}

public sealed class RiskToBrushConverter : OneWayConverter
{
    public override object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool background = string.Equals(parameter as string, "background", StringComparison.OrdinalIgnoreCase);

        string key = value switch
        {
            RiskLevel.Safe => background ? "RiskSafeBackgroundBrush" : "RiskSafeBrush",
            RiskLevel.Moderate => background ? "RiskModerateBackgroundBrush" : "RiskModerateBrush",
            RiskLevel.Advanced => background ? "RiskAdvancedBackgroundBrush" : "RiskAdvancedBrush",
            _ => background ? "ControlFillColorSecondaryBrush" : "TextFillColorTertiaryBrush",
        };

        return Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray;
    }
}

public sealed class StatusToBrushConverter : OneWayConverter
{
    public override object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string key = value switch
        {
            TweakStatus.Applied => "SystemFillColorSuccessBrush",
            TweakStatus.Partial => "SystemFillColorCautionBrush",
            TweakStatus.Unsupported => "TextFillColorDisabledBrush",
            _ => "TextFillColorTertiaryBrush",
        };

        return Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray;
    }
}

/// <summary>Turns a hex string from the profile catalog into a brush.</summary>
public sealed class HexToBrushConverter : OneWayConverter
{
    public override object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string hex && !string.IsNullOrWhiteSpace(hex))
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(hex);

                if (string.Equals(parameter as string, "soft", StringComparison.OrdinalIgnoreCase))
                {
                    color = Color.FromArgb(38, color.R, color.G, color.B);
                }

                return new SolidColorBrush(color);
            }
            catch (FormatException)
            {
                // Fall through to the neutral brush.
            }
        }

        return Application.Current?.TryFindResource("BrandBrush") as Brush ?? Brushes.MediumPurple;
    }
}

/// <summary>Formats a byte count as B / KB / MB / GB with one decimal place.</summary>
public sealed class BytesToSizeConverter : OneWayConverter
{
    public override object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Format(value is null ? 0 : System.Convert.ToInt64(value, CultureInfo.InvariantCulture), culture);

    public static string Format(long bytes, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;

        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = Math.Abs(bytes);
        int unit = 0;

        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        string number = unit == 0 ? size.ToString("0", culture) : size.ToString("0.#", culture);
        return $"{(bytes < 0 ? "-" : string.Empty)}{number} {units[unit]}";
    }
}

/// <summary>Formats megabytes for the dashboard, which works in MB throughout.</summary>
public sealed class MegabytesToSizeConverter : OneWayConverter
{
    public override object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        long megabytes = value is null ? 0 : System.Convert.ToInt64(value, CultureInfo.InvariantCulture);
        return BytesToSizeConverter.Format(megabytes * 1024 * 1024, culture);
    }
}

public sealed class TimeSpanToUptimeConverter : OneWayConverter
{
    public override object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not TimeSpan span)
        {
            return string.Empty;
        }

        return span.TotalDays >= 1
            ? $"{(int)span.TotalDays}d {span.Hours}h {span.Minutes}m"
            : span.TotalHours >= 1
                ? $"{span.Hours}h {span.Minutes}m"
                : $"{span.Minutes}m";
    }
}

/// <summary>Maps a 0-1 fraction onto a pixel width, for the inline progress bars in profile cards.</summary>
public sealed class FractionToWidthConverter : IMultiValueConverter
{
    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length < 2 ||
            values[0] is not double fraction ||
            values[1] is not double available ||
            double.IsNaN(available))
        {
            return 0d;
        }

        return Math.Max(0, Math.Min(1, fraction)) * available;
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Reports whether the active risk filter equals the level named by the parameter.</summary>
public sealed class RiskFilterEqualsConverter : OneWayConverter
{
    public override object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is RiskLevel level &&
        Enum.TryParse(parameter as string, out RiskLevel expected) &&
        level == expected;
}

/// <summary>Reports whether the applied/not-applied filter matches the parameter.</summary>
public sealed class AppliedFilterEqualsConverter : OneWayConverter
{
    public override object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool applied && (parameter as string) switch
        {
            "applied" => applied,
            "not-applied" => !applied,
            _ => false,
        };
}

/// <summary>
/// Two-way string comparison for radio-button groups: checked means "set the bound property to
/// my parameter", unchecked means "leave it alone" so the other button in the group wins.
/// </summary>
public sealed class StringEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.Equals(value as string, parameter as string, StringComparison.OrdinalIgnoreCase);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? parameter ?? Binding.DoNothing : Binding.DoNothing;
}
