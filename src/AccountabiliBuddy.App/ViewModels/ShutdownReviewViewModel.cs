using AccountabiliBuddy.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// "Today in review" on the shutdown routine: focus against the goal, drift (only with activity history on), what got done, and an
/// optional "how did today go": a 1 to 5 feeling and a one-line note, kept sealed on this PC (<see cref="ShutdownReviewStore"/>).
/// </summary>
public sealed partial class ShutdownReviewViewModel : ObservableObject
{
    public const int MaxNoteLength = ShutdownReviewStore.MaxNoteLength;

    public ShutdownReviewViewModel(string focusText, IReadOnlyList<string> doneLines, int? feeling, string note)
    {
        FocusText = focusText;
        DoneLines = doneLines;
        DriftText = string.Empty;
        Note = note;
        FeelingOptions =
        [
            new ShutdownFeelingOption(1, "1 · Rough"),
            new ShutdownFeelingOption(2, "2"),
            new ShutdownFeelingOption(3, "3 · Okay"),
            new ShutdownFeelingOption(4, "4"),
            new ShutdownFeelingOption(5, "5 · Great"),
        ];
        foreach (ShutdownFeelingOption option in FeelingOptions)
        {
            option.IsSelected = option.Value == feeling;
            option.PropertyChanged += OnOptionChanged;
        }

        Feeling = feeling;
    }

    /// <summary>E.g. "1h 05m of 1h · goal met".</summary>
    public string FocusText { get; }

    /// <summary>What got done: a count line, then up to a few titles. Empty when nothing was ticked off.</summary>
    public IReadOnlyList<string> DoneLines { get; }

    public bool HasDone => DoneLines.Count > 0;

    /// <summary>Time off track in today's sessions, filled in once it is read; empty with activity history off or nothing to say.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDrift))]
    public partial string DriftText { get; set; }

    public bool HasDrift => DriftText.Length > 0;

    public IReadOnlyList<ShutdownFeelingOption> FeelingOptions { get; }

    /// <summary>The chosen feeling, 1 to 5; null = none.</summary>
    public int? Feeling { get; private set; }

    /// <summary>One line, kept sealed on this PC.</summary>
    [ObservableProperty]
    public partial string Note { get; set; }

    /// <summary>Something was written or chosen, so there is something to keep.</summary>
    public bool HasAnswer => Feeling is not null || Note.Trim().Length > 0;

    private void OnOptionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ShutdownFeelingOption.IsSelected) || sender is not ShutdownFeelingOption { IsSelected: true } chosen)
        {
            return;
        }

        // One at a time.
        foreach (ShutdownFeelingOption other in FeelingOptions.Where(option => option != chosen && option.IsSelected))
        {
            other.IsSelected = false;
        }

        Feeling = chosen.Value;
    }
}
