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
/// <param name="Indented">Set in under the heading above it (a category listed under its device).</param>
/// <param name="IsHeading">A group's name above its entries ("Group by", the task's title): a small muted label, not clickable.</param>
/// <param name="IsDanger">Drawn in the drift (red) colour: Delete, the View menu's Reset. A row menu puts it last (brief §3.6).</param>
/// <param name="Value">Muted text at the right: what the entry is set to now ("Next", "50 min", "142c", "1 of 5", "2 of 3"). Never a key.</param>
/// <param name="Shortcut">
/// The key that runs this entry on a row or the page ("F2", "Del", "H"; <see cref="Services.TaskShortcuts"/>). Never shown in a menu:
/// menus have no shortcut letters (brief §3.6, CN §4b).
/// </param>
/// <param name="Note">A muted second line under the text ("How the day starts and ends, the start-of-day card").</param>
/// <param name="Segments">
/// The entry's choices drawn inline at the right as a small segmented control (round 2 board Goals-PC › Options: "Week starts on
/// Mon · Sun · Sat"): each has its text, its command and <see cref="IsChecked"/> for the one in force. Clicking one picks it and keeps the
/// menu open; Enter on the entry moves to the next choice.
/// </param>
/// <param name="OpensMore">Opens a panel or another page (Reminders &amp; due date, "Tasks settings"): drawn with › like a submenu, and → opens it too.</param>
public sealed record TaskMenuEntry(
    string Header,
    ICommand? Command = null,
    IReadOnlyList<TaskMenuEntry>? Children = null,
    bool IsChecked = false,
    IBrush? Dot = null,
    string? ViewAction = null,
    bool IsSeparator = false,
    MaterialIconKind? Icon = null,
    bool Indented = false,
    bool IsHeading = false,
    bool IsDanger = false,
    string? Value = null,
    string? Shortcut = null,
    string? Note = null,
    bool OpensMore = false,
    IReadOnlyList<TaskMenuEntry>? Segments = null)
{
    public static TaskMenuEntry Separator { get; } = new(string.Empty, IsSeparator: true);

    public static TaskMenuEntry Async(string header, Func<Task> run, MaterialIconKind? icon = null) => new(header, new AsyncRelayCommand(run), Icon: icon);

    public static TaskMenuEntry Do(string header, Action run, MaterialIconKind? icon = null) => new(header, new RelayCommand(run), Icon: icon);

    public static TaskMenuEntry View(string header, string viewAction, MaterialIconKind? icon = null) => new(header, ViewAction: viewAction, Icon: icon);

    public static TaskMenuEntry Group(string header, IReadOnlyList<TaskMenuEntry> children, MaterialIconKind? icon = null) => new(header, Children: children, Icon: icon);
}
