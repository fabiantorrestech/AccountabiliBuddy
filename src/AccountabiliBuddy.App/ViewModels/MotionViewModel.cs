using AccountabiliBuddy.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>Settings → On screen &amp; sound → Animations: Normal, Fast, Slow or Off; applies at once.</summary>
public sealed partial class MotionViewModel : ObservableObject
{
    private readonly Motion _motion;

    /// <summary>The theme: as Windows, light or dark (set by the app).</summary>
    public AccountabiliBuddy.App.Services.AppTheme? Theme
    {
        get => _theme;
        init
        {
            _theme = value;
            _themeIndex = value?.Choice ?? 0;
        }
    }

    private readonly AccountabiliBuddy.App.Services.AppTheme? _theme;
    private int _themeIndex;

    public static IReadOnlyList<string> ThemeNames => AccountabiliBuddy.App.Services.AppTheme.Names;

    public int ThemeIndex
    {
        get => _themeIndex;
        set
        {
            if (SetProperty(ref _themeIndex, value) && _theme is not null)
            {
                _ = _theme.SetAsync(value);
            }
        }
    }

    public MotionViewModel(Motion motion)
    {
        _motion = motion;
        SpeedIndex = (int)motion.Settings.Speed;
    }

    /// <summary>In the order of <see cref="MotionSpeed"/>.</summary>
    public static IReadOnlyList<string> SpeedNames { get; } = ["Normal", "Fast", "Slow", "Off"];

    [ObservableProperty]
    public partial int SpeedIndex { get; set; }

    partial void OnSpeedIndexChanged(int value)
    {
        var speed = (MotionSpeed)Math.Clamp(value, 0, SpeedNames.Count - 1);
        if (speed != _motion.Settings.Speed)
        {
            _ = _motion.SaveAsync(_motion.Settings with { Speed = speed });
        }
    }
}
