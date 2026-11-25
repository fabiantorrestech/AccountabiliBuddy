using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// One tag chip in Move and label's Tags (checklist 8c.51-8c.55), like the right-click Tags menu: ☑ every task being edited has it, ⊟ some
/// ("1 of 2"), ☐ none; picked to go on ("on for all") or to come off ("off all"). A click picks it, a second click takes the pick back.
/// </summary>
public sealed partial class MoveAndLabelTagViewModel : ObservableObject
{
    public MoveAndLabelTagViewModel(string tag, Action<string> click)
    {
        Tag = tag;
        Note = string.Empty;
        ClickCommand = new RelayCommand(() => click(tag));
    }

    /// <summary>The tag without its "#".</summary>
    public string Tag { get; }

    public string Text => "#" + Tag;

    public IRelayCommand ClickCommand { get; }

    /// <summary>The pick on it: null for none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAdding), nameof(IsRemoving), nameof(Icon), nameof(IsPicked))]
    public partial MoveAndLabelTagPick? Pick { get; set; }

    /// <summary>How many of the tasks being edited have it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Icon), nameof(IsHad))]
    public partial int Having { get; set; }

    /// <summary>How many tasks are being edited.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Icon))]
    public partial int Of { get; set; }

    /// <summary>"on for all", "off all", "1 of 2", or "".</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNote))]
    public partial string Note { get; set; }

    public bool HasNote => Note.Length > 0;

    public bool IsAdding => Pick == MoveAndLabelTagPick.Add;

    public bool IsRemoving => Pick == MoveAndLabelTagPick.Remove;

    public bool IsPicked => Pick is not null;

    /// <summary>Some or all of the tasks being edited have it (the icon is Muted, not Faint).</summary>
    public bool IsHad => Having > 0;

    public bool AllHaveIt => Of > 0 && Having == Of;

    /// <summary>☑ going on or all have it; ☐ coming off or none; ⊟ some.</summary>
    public MaterialIconKind Icon => Pick switch
    {
        MoveAndLabelTagPick.Add => MaterialIconKind.CheckboxMarked,
        MoveAndLabelTagPick.Remove => MaterialIconKind.CheckboxBlankOutline,
        _ => AllHaveIt ? MaterialIconKind.CheckboxMarked : Having > 0 ? MaterialIconKind.MinusBoxOutline : MaterialIconKind.CheckboxBlankOutline,
    };
}
