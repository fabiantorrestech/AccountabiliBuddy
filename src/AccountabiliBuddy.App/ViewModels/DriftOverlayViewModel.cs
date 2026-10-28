using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// Content of the dimmed overlay on a drifting monitor. Text is updated in place while the overlay stays open.
/// The presenter sets the button labels and actions to match the block's scope.
/// </summary>
public sealed partial class DriftOverlayViewModel : ObservableObject
{
    private Action? _primary;
    private Action? _secondary;

    /// <summary>"This isn't QS - Default." (the session's task or profile).</summary>
    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    /// <summary>The third step of the ladder, as the progress line names it ("3 · Close tab").</summary>
    [ObservableProperty]
    public partial string NextStepText { get; set; } = "3 · Next step";

    [ObservableProperty]
    public partial string Target { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SessionDetail { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? Hint { get; set; }

    [ObservableProperty]
    public partial string PrimaryLabel { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSecondary))]
    public partial string? SecondaryLabel { get; set; }

    public bool HasSecondary => SecondaryLabel is not null;

    public void SetActions(string primaryLabel, Action primary, string? secondaryLabel, Action? secondary)
    {
        PrimaryLabel = primaryLabel;
        _primary = primary;
        SecondaryLabel = secondaryLabel;
        _secondary = secondary;
    }

    [RelayCommand]
    private void Primary()
    {
        _primary?.Invoke();
    }

    [RelayCommand]
    private void Secondary()
    {
        _secondary?.Invoke();
    }
}
