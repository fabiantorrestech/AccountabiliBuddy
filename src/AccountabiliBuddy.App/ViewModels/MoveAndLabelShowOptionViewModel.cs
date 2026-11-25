using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One option of Move and label's Show list: "All", "Not done yet" or "Done", its count, and whether it is the one shown.</summary>
public sealed class MoveAndLabelShowOptionViewModel
{
    public MoveAndLabelShowOptionViewModel(MoveAndLabelShow show, string name, int count, bool isPicked, Action<MoveAndLabelShow> pick)
    {
        Show = show;
        Name = name;
        CountText = "(" + count.ToString(System.Globalization.CultureInfo.CurrentCulture) + ")";
        IsPicked = isPicked;
        PickCommand = new RelayCommand(() => pick(show));
    }

    public MoveAndLabelShow Show { get; }

    public string Name { get; }

    /// <summary>"(4)", shown in Muted after the name.</summary>
    public string CountText { get; }

    public bool IsPicked { get; }

    public IRelayCommand PickCommand { get; }
}
