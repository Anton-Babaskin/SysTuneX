using System.Globalization;
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
