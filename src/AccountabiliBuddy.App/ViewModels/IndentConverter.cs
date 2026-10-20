using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>True = indented under a device row (the Profiles tab once a device is linked); false = no indent.</summary>
public sealed class IndentConverter : IValueConverter
{
    public static IndentConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? new Thickness(20, 0, 0, 6) : new Thickness(0);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
