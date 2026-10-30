using Avalonia.Data.Converters;
using Avalonia.Media;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>Converters for the Ctrl+Alt panel's rows.</summary>
public static class PanelFontWeights
{
    /// <summary>The current task is bold, the others regular.</summary>
    public static IValueConverter Current { get; } = new FuncValueConverter<bool, FontWeight>(isCurrent => isCurrent ? FontWeight.SemiBold : FontWeight.Normal);
}
