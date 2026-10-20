using System.Globalization;
using Avalonia.Data.Converters;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>True (a day that has passed) = greyed out; false = fully shown.</summary>
public sealed class PastOpacityConverter : IValueConverter
{
    public static PastOpacityConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? 0.5 : 1.0;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
