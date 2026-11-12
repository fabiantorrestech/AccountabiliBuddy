using System.Globalization;
using Avalonia.Data.Converters;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// True when the bound section (<see cref="SettingsViewModel.SelectedSection"/>) is the one given as the parameter: which Settings page
/// shows, by the page's identity rather than its place in the list.
/// </summary>
public sealed class SettingsSectionIs : IValueConverter
{
    public static SettingsSectionIs Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is SettingsSection shown && parameter is SettingsSection wanted && shown == wanted;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
