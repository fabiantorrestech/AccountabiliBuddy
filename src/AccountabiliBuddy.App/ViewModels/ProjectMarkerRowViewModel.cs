using System.Windows.Input;
using AccountabiliBuddy.Core.Tasks;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// One project marker on Settings › Tasks › Title help: a style the user taught ("⟦Name⟧", "Name ›") or one they said No to when the
/// add box offered to learn it. Remove forgets it (a taught one stops being spotted; a declined one may be offered again).
/// </summary>
public sealed class ProjectMarkerRowViewModel
{
    public ProjectMarkerRowViewModel(ProjectMarkerPattern pattern, bool isDeclined, ICommand remove)
    {
        Pattern = pattern;
        IsDeclined = isDeclined;
        RemoveCommand = remove;
    }

    public ProjectMarkerPattern Pattern { get; }

    public bool IsDeclined { get; }

    /// <summary>"⟦Name⟧" or "Name ›".</summary>
    public string Text => Pattern.Open + "Name" + Pattern.Close;

    /// <summary>"Spotted" or "Not offered again".</summary>
    public string KindText => IsDeclined ? "Not offered again" : "Spotted";

    public string RemoveTip => IsDeclined ? "Forget the No: the add box may offer it again" : "Stop spotting this marker";

    public ICommand RemoveCommand { get; }
}
