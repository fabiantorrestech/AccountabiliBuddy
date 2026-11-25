using AccountabiliBuddy.App.Services.Theming;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One row of the title-bar menu's theme list (Tasks redesign 9.22, 9.23): a swatch, the name, a tick on the current one.</summary>
public sealed partial class ThemeChoiceViewModel : ObservableObject
{
    public ThemeChoiceViewModel(ThemeName theme)
    {
        Theme = theme;
        Name = ThemeToggleViewModel.NameOf(theme);
    }

    public ThemeName Theme { get; }

    public string Name { get; }

    [ObservableProperty]
    public partial bool IsCurrent { get; set; }

    /// <summary>The swatch's stripes (2 : 2 : 1): the theme's Bg, Surface2 and Accent in the light or dark now shown.</summary>
    [ObservableProperty]
    public partial IBrush BgBrush { get; set; } = Brushes.Gray;

    [ObservableProperty]
    public partial IBrush Surface2Brush { get; set; } = Brushes.Gray;

    [ObservableProperty]
    public partial IBrush AccentBrush { get; set; } = Brushes.Gray;

    /// <summary>Reads the swatch again for light or dark.</summary>
    public void ShowSwatch(bool dark)
    {
        (Color bg, Color surface2, Color accent) = ThemeSwatches.Of(Theme, dark);
        BgBrush = new SolidColorBrush(bg);
        Surface2Brush = new SolidColorBrush(surface2);
        AccentBrush = new SolidColorBrush(accent);
    }
}
