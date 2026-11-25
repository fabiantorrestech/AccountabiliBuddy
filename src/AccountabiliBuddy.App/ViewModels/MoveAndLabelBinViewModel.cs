using AccountabiliBuddy.Core.Domain;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// One of Move and label's four bin buttons (Today, Next, To sort, Later; checklist 8c.43-8c.46): its icon and name, a "Current" or
/// "2 here" tag for the tasks being edited, and whether it is picked (a second click takes the pick back).
/// </summary>
public sealed partial class MoveAndLabelBinViewModel : ObservableObject
{
    public MoveAndLabelBinViewModel(TaskBin bin, string name, Action<TaskBin> pick)
    {
        Bin = bin;
        Name = name;
        Icon = TaskBinIcons.Redesigned(bin);
        Tag = string.Empty;
        PickCommand = new RelayCommand(() => pick(bin));
    }

    public TaskBin Bin { get; }

    /// <summary>"Today" (Queued), "Next", "To sort", "Later".</summary>
    public string Name { get; }

    public MaterialIconKind Icon { get; }

    public IRelayCommand PickCommand { get; }

    /// <summary>"Current" when every task being edited is in this bin, "2 here" when some are, "" when none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTag))]
    public partial string Tag { get; set; }

    public bool HasTag => Tag.Length > 0;

    [ObservableProperty]
    public partial bool IsPicked { get; set; }
}
