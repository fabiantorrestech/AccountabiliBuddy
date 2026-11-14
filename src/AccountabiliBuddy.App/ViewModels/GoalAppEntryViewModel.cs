using AccountabiliBuddy.Core.Goals;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// One app or site of a screen-time source in Edit goal (ST2): its kind chip, its text, a note and a remove (×). The text is private text of
/// the name class: it stays in this dialog and the sealed goal store, and is never logged. Keeps the entry it came from, so fields a newer
/// app wrote on it travel on when nothing about it changed.
/// </summary>
public sealed partial class GoalAppEntryViewModel : ObservableObject
{
    private readonly GoalAppMatch? _original;
    private readonly Func<GoalAppMatch, bool> _wasSeen;
    private readonly Action<GoalAppEntryViewModel> _remove;
    private readonly Action? _changed;

    public GoalAppEntryViewModel(GoalAppMatch match, GoalAppMatch? original, Func<GoalAppMatch, bool> wasSeen, Action<GoalAppEntryViewModel> remove, Action changed)
    {
        _original = original;
        _wasSeen = wasSeen;
        _remove = remove;
        Text = match.Text;
        SelectedKind = GoalAppKindChoice.Of(match.Kind);
        _changed = changed; // set last: the first kind is not a change
    }

    public IReadOnlyList<GoalAppKindChoice> Kinds => GoalAppKindChoice.All;

    public string Text { get; }

    [ObservableProperty]
    public partial GoalAppKindChoice SelectedKind { get; set; }

    /// <summary>"Counts on PCs only · Not seen on this device yet": where the entry counts, and whether this PC's last 30 days know it.</summary>
    public string? Note
    {
        get
        {
            var match = ToMatch();
            string? where = match.Kind switch
            {
                GoalAppMatchKind.Package => "Counts on phones only",
                GoalAppMatchKind.Process or GoalAppMatchKind.Website => "Counts on PCs only",
                _ => null,
            };
            string? seen = match.Kind != GoalAppMatchKind.Package && !_wasSeen(match) ? "Not seen on this device yet" : null;
            return where is null ? seen : seen is null ? where : where + " · " + seen;
        }
    }

    public bool HasNote => Note is not null;

    /// <summary>The entry as it is set now (the original object when nothing changed, so unknown fields stay).</summary>
    public GoalAppMatch ToMatch()
    {
        return _original is not null && _original.Kind == SelectedKind.Kind && _original.Text == Text ? _original : new GoalAppMatch(SelectedKind.Kind, Text);
    }

    [RelayCommand]
    private void Remove() => _remove(this);

    partial void OnSelectedKindChanged(GoalAppKindChoice value)
    {
        OnPropertyChanged(nameof(Note));
        OnPropertyChanged(nameof(HasNote));
        _changed?.Invoke();
    }
}
