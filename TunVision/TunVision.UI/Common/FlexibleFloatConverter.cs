using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TunVision.UI.Common;

/// <summary>
/// Converts float / double / int values to string and back using InvariantCulture,
/// accepting both '.' and ',' as decimal separators so users can type decimals freely.
/// </summary>
public class FlexibleFloatConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is float f)
            return f.ToString("G", CultureInfo.InvariantCulture);
        if (value is double d)
            return d.ToString("G", CultureInfo.InvariantCulture);
        if (value is int i)
            return i.ToString(CultureInfo.InvariantCulture);
        return value?.ToString() ?? "";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string s = (value as string)?.Trim() ?? "";
        if (string.IsNullOrEmpty(s) || s == "-" || s == "." || s == ",")
            return DependencyProperty.UnsetValue;

        // Accept both '.' and ',' as decimal point
        string normalized = s.Replace(',', '.');

        if (targetType == typeof(float) || targetType == typeof(float?))
        {
            if (float.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out float fVal))
                return fVal;
            return DependencyProperty.UnsetValue;
        }

        if (targetType == typeof(double) || targetType == typeof(double?))
        {
            if (double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out double dVal))
                return dVal;
            return DependencyProperty.UnsetValue;
        }

        if (targetType == typeof(int) || targetType == typeof(int?))
        {
            if (int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out int iVal))
                return iVal;
            if (double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out double dInt))
                return (int)Math.Round(dInt);
            return DependencyProperty.UnsetValue;
        }

        return DependencyProperty.UnsetValue;
    }
}
