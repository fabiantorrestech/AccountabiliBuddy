using System.Windows.Input;
using Avalonia.Media;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// One task in the History popover, built like a Tasks page column row (PC fixes, owner 0.1.132): the label's dot, the title (two
/// lines, then an ellipsis), a sub-line (label name, time limit, #tags, then "Finished Tue 11/10 4:05 PM" or Deleted / Archived) and
/// one ⋯ at the right whose menu holds its action: Do again (finished) or Bring back (deleted or archived).
/// </summary>
public sealed record TaskHistoryRowViewModel(string Title, string Sub, string ActionText, MaterialIconKind ActionIcon, ICommand Command)
{
    /// <summary>The label's name; empty for none.</summary>
    public string LabelName { get; init; } = string.Empty;

    public bool HasLabel => LabelName.Length > 0;

    /// <summary>The label's colour for the leading dot; transparent with none (a hollow dot shows instead).</summary>
    public IBrush LabelBrush { get; init; } = Brushes.Transparent;

    /// <summary>The sub-line's pieces after the label's name: time limit, #tags, when it finished or was deleted.</summary>
    public IReadOnlyList<TaskRowBit> Bits { get; init; } = [];

    /// <summary>The sub-line as words ("Work · 25 min · #home · Finished Tue 11/10 4:05 PM"): the row's accessible detail.</summary>
    public string DetailText => string.Join(" · ", new[] { LabelName }.Concat(Bits.Select(bit => bit.Text)).Where(part => part.Length > 0));

    /// <summary>The ⋯ menu: the task's title as its heading, then the row's action.</summary>
    public IReadOnlyList<TaskMenuEntry> Menu()
    {
        return [new TaskMenuEntry(Title, IsHeading: true), new TaskMenuEntry(ActionText, Command, Icon: ActionIcon)];
    }
}
