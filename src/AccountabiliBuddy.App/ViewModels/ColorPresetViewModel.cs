using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One swatch of the colour editor's preset grid.</summary>
public sealed partial class ColorPresetViewModel : ObservableObject
{
    private readonly Action<string> _pick;

    public ColorPresetViewModel(string hex, Action<string> pick)
    {
        Hex = hex;
        _pick = pick;
        Brush = TaskCategoryBrushes.Of(hex);
    }

    public string Hex { get; }

    public IBrush Brush { get; }

    /// <summary>The draft is exactly this colour.</summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [RelayCommand]
    private void Pick() => _pick(Hex);
}
