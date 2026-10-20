using AccountabiliBuddy.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The noise player's controls: the sidebar's mini player and the on-screen window's play/pause and volume (shown
/// while Ctrl+Alt is held). Both show this one instance.
/// </summary>
public sealed partial class NoisePlayerViewModel : ObservableObject
{
    private readonly NoisePlayerService _noise;

    // True while showing the service's state, so the change isn't sent back to it.
    private bool _showing;

    public NoisePlayerViewModel(NoisePlayerService noise)
    {
        _noise = noise;
        TrackText = string.Empty;
        _noise.Changed += (_, _) => Show();
        Show();
    }

    /// <summary>The file playing or next, or why there's nothing to play.</summary>
    [ObservableProperty]
    public partial string TrackText { get; set; }

    /// <summary>The name being typed after the pencil was clicked.</summary>
    [ObservableProperty]
    public partial bool IsRenaming { get; set; }

    [ObservableProperty]
    public partial string RenameText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasFiles { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlayGlyph), nameof(PlayTip))]
    public partial bool IsPlaying { get; set; }

    /// <summary>Segoe Fluent Icons: Pause or Play.</summary>
    public string PlayGlyph => IsPlaying ? "" : "";

    public string PlayTip => IsPlaying ? "Pause the noise" : "Play the noise";

    /// <summary>0–100.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VolumeText))]
    public partial double Volume { get; set; }

    public string VolumeText => $"{Volume:0}%";

    [ObservableProperty]
    public partial bool Repeat { get; set; }

    partial void OnVolumeChanged(double value)
    {
        if (!_showing)
        {
            _noise.SetVolume((int)Math.Round(value));
        }
    }

    partial void OnRepeatChanged(bool value)
    {
        if (!_showing)
        {
            _noise.SetRepeat(value);
        }
    }

    [RelayCommand]
    private void PlayPause()
    {
        _noise.TogglePlay();
    }

    [RelayCommand]
    private void Next()
    {
        _noise.Next();
    }

    [RelayCommand]
    private void Previous()
    {
        _noise.Previous();
    }

    [RelayCommand]
    private void StartRename()
    {
        if (!HasFiles)
        {
            return;
        }

        RenameText = TrackText;
        IsRenaming = true;
    }

    [RelayCommand]
    private async Task SaveRenameAsync()
    {
        IsRenaming = false;
        await _noise.RenameCurrentAsync(RenameText);
    }

    [RelayCommand]
    private void CancelRename()
    {
        IsRenaming = false;
    }

    private void Show()
    {
        _showing = true;
        NoiseSettings settings = _noise.Settings;
        HasFiles = _noise.HasFiles;
        TrackText = _noise.CurrentName ?? "No sound files in the folder (choose one in Settings)";
        IsPlaying = _noise.IsPlaying;
        Volume = settings.Volume;
        Repeat = settings.Repeat;
        _showing = false;
    }
}
