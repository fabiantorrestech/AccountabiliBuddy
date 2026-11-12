using System.Windows.Input;
using AccountabiliBuddy.Core.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// One numbered decision of the weekly tidy-up (spec 05 §9), Claude-Code style: "1." Keep, "2." Move to Next, "3." Archive, "4."
/// Delete (red). The highlighted one has the ❯ caret, the accent border and tint (red for Delete); the suggestion carries "Suggested",
/// a decision made before "Your choice".
/// </summary>
public sealed partial class TidyUpDecisionViewModel(TidyUpChoice choice, int number, string title, string sub, MaterialIconKind icon, ICommand pick) : ObservableObject
{
    public TidyUpChoice Choice { get; } = choice;

    public string Number { get; } = number + ".";

    public string Title { get; } = title;

    public string Sub { get; } = sub;

    public MaterialIconKind Icon { get; } = icon;

    public ICommand Pick { get; } = pick;

    public bool IsDanger => Choice == TidyUpChoice.Delete;

    public string Name => Number + " " + Title + ": " + Sub;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLitDanger))]
    public partial bool IsLit { get; set; }

    public bool IsLitDanger => IsLit && IsDanger;

    [ObservableProperty]
    public partial bool IsSuggested { get; set; }

    [ObservableProperty]
    public partial bool IsChosen { get; set; }
}
