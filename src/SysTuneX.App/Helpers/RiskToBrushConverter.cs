using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using SysTuneX.Core.Models;

namespace SysTuneX.App.Helpers;

public class RiskToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return (RiskLevel)value switch
        {
            RiskLevel.Safe => new SolidColorBrush(Color.FromRgb(32, 192, 32)),
            RiskLevel.Moderate => new SolidColorBrush(Color.FromRgb(255, 165, 0)),
            RiskLevel.Advanced => new SolidColorBrush(Color.FromRgb(255, 68, 68)),
            _ => new SolidColorBrush(Colors.Gray)
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public class StatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return (TweakStatus)value switch
        {
            TweakStatus.Applied => new SolidColorBrush(Color.FromRgb(32, 192, 32)),
            TweakStatus.NotApplied => new SolidColorBrush(Color.FromRgb(150, 150, 150)),
            TweakStatus.Error => new SolidColorBrush(Color.FromRgb(255, 68, 68)),
            _ => new SolidColorBrush(Colors.Gray)
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public class BoolToRunningTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        (bool)value ? "Running" : "Stopped";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public class BytesToMbConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var bytes = System.Convert.ToInt64(value);
        return $"{bytes / (1024.0 * 1024.0):F1} MB";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>bool → bool (inverted) for IsEnabled bindings</summary>
public class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool b && !b;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool b && !b;
}

/// <summary>bool → Visibility (false = Visible, true = Collapsed)</summary>
public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool b && b ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>int == 0 → Visible, int > 0 → Collapsed (for "empty list" labels)</summary>
public class ZeroToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        System.Convert.ToInt32(value) == 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>string → bool: non-empty string = true (for InfoBar IsOpen binding)</summary>
public class NonEmptyStringToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        !string.IsNullOrEmpty(value?.ToString());

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>RiskLevel → semi-transparent background SolidColorBrush (alpha=40)</summary>
public class RiskToBackgroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return (RiskLevel)value switch
        {
            RiskLevel.Safe     => new SolidColorBrush(Color.FromArgb(40, 32, 192, 32)),
            RiskLevel.Moderate => new SolidColorBrush(Color.FromArgb(40, 255, 165, 0)),
            RiskLevel.Advanced => new SolidColorBrush(Color.FromArgb(40, 255, 68, 68)),
            _                  => new SolidColorBrush(Color.FromArgb(40, 150, 150, 150))
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
