using System.Globalization;
using System.Windows.Data;
using AdvancedControllerProcessor.Models;

namespace AdvancedControllerProcessor.Converters;

/// <summary>
/// Converts boolean to status icon/color for controller status display.
/// </summary>
public sealed class BoolToStatusConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool isActive = value is bool b && b;
        string mode = parameter as string ?? "icon";

        return mode switch
        {
            "color" => isActive ? "#22C55E" : "#EF4444",
            "text" => isActive ? "Active" : "Inactive",
            _ => isActive ? "\u25CF" : "\u25CB" // Filled/empty circle
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

/// <summary>
/// Converts boolean to its inverse.
/// </summary>
public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b)
            return !b;
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b)
            return !b;
        return false;
    }
}

/// <summary>
/// Converts float to formatted string for display.
/// </summary>
public sealed class FloatToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is float f)
        {
            string format = parameter as string ?? "F3";
            return f.ToString(format, CultureInfo.InvariantCulture);
        }
        return "0.000";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string s && float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float result))
            return result;
        return 0f;
    }
}

/// <summary>
/// Converts ConnectionType enum to display string.
/// </summary>
public sealed class ConnectionTypeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is ConnectionType ct)
            return ct switch
            {
                ConnectionType.USB => "USB",
                ConnectionType.Bluetooth => "Bluetooth",
                _ => "Unknown"
            };
        return "Unknown";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}
