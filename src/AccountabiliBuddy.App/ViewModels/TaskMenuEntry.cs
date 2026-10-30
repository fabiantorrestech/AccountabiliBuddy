using System.Windows.Input;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// One entry of a ⋯ menu on a task or a step: a command, a group of entries (a submenu), a separator, or an item the view
/// answers itself (<paramref name="ViewAction"/>, e.g. opening the row's own editor). The menu is built when it opens
/// (<see cref="ITaskMenuSource"/>), so a row costs nothing until its menu is used.
/// </summary>
/// <param name="Dot">A category's colour dot before the text.</param>
/// <param name="IsChecked">Marked with ✓ (the category or plan part the task has now).</param>
/// <param name="Icon">A 16 px Material icon before the text (ADR 32: the same meaning has the same icon everywhere); null for none.</param>
public sealed record TaskMenuEntry(
    string Header,
    ICommand? Command = null,
    IReadOnlyList<TaskMenuEntry>? Children = null,
    bool IsChecked = false,
    IBrush? Dot = null,
    string? ViewAction = null,
    bool IsSeparator = false,
    MaterialIconKind? Icon = null)
{
    public static TaskMenuEntry Separator { get; } = new(string.Empty, IsSeparator: true);

    public static TaskMenuEntry Async(string header, Func<Task> run, MaterialIconKind? icon = null) => new(header, new AsyncRelayCommand(run), Icon: icon);

    public static TaskMenuEntry Do(string header, Action run, MaterialIconKind? icon = null) => new(header, new RelayCommand(run), Icon: icon);

    public static TaskMenuEntry View(string header, string viewAction, MaterialIconKind? icon = null) => new(header, ViewAction: viewAction, Icon: icon);

    public static TaskMenuEntry Group(string header, IReadOnlyList<TaskMenuEntry> children, MaterialIconKind? icon = null) => new(header, Children: children, Icon: icon);
}
