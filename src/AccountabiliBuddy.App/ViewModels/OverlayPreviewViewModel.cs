using AccountabiliBuddy.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The live preview beside the overlay settings: which scene shows, session or idle, and the settings as they would be saved
/// (<see cref="OverlaySettingsViewModel.Draft"/>), so the picture follows every click before the save lands. Display only: nothing here is
/// stored or synced, it has no timers, and the two sample tiles read no service. A scene jumps to the group of settings the user just changed;
/// the tabs still let them look at any other by hand. Use from the UI thread only.
/// </summary>
public sealed partial class OverlayPreviewViewModel : ObservableObject
{
    public static IReadOnlyList<string> SceneNames { get; } = ["Taskbar", "Auto-hide", "Full screen", "Panel"];

    public static IReadOnlyList<string> StateNames { get; } = ["Session", "Idle"];

    private readonly OverlaySettingsViewModel _source;

    public OverlayPreviewViewModel(OverlaySettingsViewModel source)
    {
        _source = source;
        SessionTile = TaskbarOverlayViewModel.CreatePreview(true);
        IdleTile = TaskbarOverlayViewModel.CreatePreview(false);
        Settings = source.Draft();
        source.PreviewChanged += OnSourceChanged;
        ApplyLayout();
    }

    /// <summary>Raised on any change the picture depends on; the view draws the scene again.</summary>
    public event EventHandler? Changed;

    public TaskbarOverlayViewModel SessionTile { get; }

    public TaskbarOverlayViewModel IdleTile { get; }

    /// <summary>The tile the scenes draw: the session sample, or the idle one.</summary>
    public TaskbarOverlayViewModel Tile => ShowsSession ? SessionTile : IdleTile;

    /// <summary>The settings as they would be saved.</summary>
    public OverlaySettings Settings { get; private set; }

    /// <summary>Index into <see cref="SceneNames"/>.</summary>
    [ObservableProperty]
    public partial int SceneIndex { get; set; }

    public OverlayPreviewScene Scene => (OverlayPreviewScene)Math.Clamp(SceneIndex, 0, SceneNames.Count - 1);

    /// <summary>Index into <see cref="StateNames"/>: 0 draws the running session, 1 nothing running.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsSession), nameof(Tile))]
    public partial int StateIndex { get; set; }

    public bool ShowsSession => StateIndex == 0;

    /// <summary>The mock taskbar's app icons start at the left end (Windows 10, or Windows 11 set to left) instead of the middle.</summary>
    [ObservableProperty]
    public partial bool IconsLeft { get; set; }

    /// <summary>The narrow layout's strip is folded to its title row (kept for this run of the app only).</summary>
    [ObservableProperty]
    public partial bool IsCollapsed { get; set; }

    partial void OnSceneIndexChanged(int value)
    {
        OnPropertyChanged(nameof(Scene));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    partial void OnStateIndexChanged(int value) => Changed?.Invoke(this, EventArgs.Empty);

    partial void OnIconsLeftChanged(bool value) => Changed?.Invoke(this, EventArgs.Empty);

    private void OnSourceChanged(object? sender, OverlayPreviewChange change)
    {
        Settings = _source.Draft();
        ApplyLayout();
        if (change.Scene is { } jump)
        {
            SceneIndex = (int)jump;
        }

        if (change.LeftAlignedChosen)
        {
            // "Follow my apps" only means something on a taskbar whose icons start at the left, so the mock shows one.
            IconsLeft = Settings.LeftAligned == LeftAlignedMode.FollowApps;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
    private void ApplyLayout()
    {
        SessionTile.ApplyLayout(Settings);
        IdleTile.ApplyLayout(Settings);
    }
}