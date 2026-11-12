using AccountabiliBuddy.Core.Tasks;

namespace AccountabiliBuddy.App.ViewModels;

// The keyboard map's page side (spec 03 §E, Implementation Notes 13k): which menu Del / F2 / H act through, Ctrl+G's go to a task by
// its ID, Ctrl+Z, and the page's part of Esc. The keys themselves are read by Services/TaskShortcuts (the window) and the rows.
public sealed partial class TasksViewModel
{
    /// <summary>Ctrl+G found a task on the Shelf: the Tasks page puts the keyboard on its row (after the page shows it).</summary>
    public event EventHandler<TaskRowKey>? FocusRowRequested;

    /// <summary>Ctrl+G found a task of Today's sections (round 2 R3): the Today page puts the keyboard on its row.</summary>
    public event EventHandler<TaskRowKey>? FocusTodayRowRequested;

    /// <summary>
    /// The menu a key acts through (Del, F2, H): the selection's bulk menu when two or more rows are picked; else the one picked row's,
    /// the row with the keyboard (<paramref name="focused"/>), or the open task's (only where it shows: <paramref name="onTasksPage"/>).
    /// A key runs the entry the menu shows, so it follows exactly the menus' rules (a running session's own tasks included). Empty when
    /// nothing is targeted.
    /// </summary>
    internal IReadOnlyList<TaskMenuEntry> ShortcutMenu(TaskListRowViewModel? focused, bool onTasksPage)
    {
        if (_picked.Count >= 2)
        {
            return SelectionMenu();
        }

        if (_picked.Count == 1)
        {
            return MenuFor(_picked.First());
        }

        if (focused is not null)
        {
            return focused.BuildContextMenu();
        }

        return onTasksPage && IsDetailOpen && _selectedKey is TaskRowKey open ? MenuFor(open) : [];
    }

    /// <summary>Ctrl+Z: the toast's Undo, else the newest of the last 10 task actions. False when there was nothing to undo.</summary>
    public Task<bool> UndoLatestAsync() => Toasts.UndoLatestAsync();

    /// <summary>
    /// Esc on the page when nothing inside it took the key: the selection clears first, then the open task closes (only where it shows,
    /// <paramref name="onTasksPage"/>). False when neither applied.
    /// </summary>
    public bool Escape(bool onTasksPage)
    {
        if (ClearSelection())
        {
            return true;
        }

        if (onTasksPage && IsDetailOpen)
        {
            CloseDetailCommand.Execute(null);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Ctrl+G: finds the task with this ID ("142", or "142b" for its step). On the Shelf: the Tasks page shows (and the Shelf, if hidden
    /// during a session), its column unfolds, the task opens and its row takes the keyboard. In Today's sections (This session, Queued,
    /// Reminders due today, Finished today; round 2 R3): the Today page shows, the section unfolds and the row takes the keyboard (Enter
    /// opens it on Tasks). Null when found; otherwise what to say ("No task 142"). Only tasks the pages list are found (the Shelf's
    /// view may leave one out: it says so).
    /// </summary>
    public string? GoToTask(string reference)
    {
        string text = reference.Trim().TrimStart('#').ToLowerInvariant();
        if (!TaskSearch.IsId(text))
        {
            return "Type a task ID, like 142 or 142b";
        }

        if (_store is null)
        {
            return "Task IDs come with the task list";
        }

        if (TaskSearch.Find(_store.Lists.Tasks.Select(item => item.Task), text).FirstOrDefault() is not { } hit)
        {
            return "No task " + text;
        }

        Guid id = hit.Task.Id;
        // The Shelf first: a Shelf task with a reminder due today is also listed under Today's Reminders due today.
        foreach (TaskSectionsPanelViewModel panel in new[] { Shelf.Panel, TodayTabPanel })
        {
            foreach (TaskSectionViewModel section in panel.Sections)
            {
                // The task's own row; in a running session's Quiet view it may show only as its focused steps' rows.
                TaskListRowViewModel? row = section.Rows.FirstOrDefault(candidate => candidate.Id == id && candidate.OwnerKey is null)
                    ?? section.Rows.FirstOrDefault(candidate => candidate.OwnerKey?.Id == id);
                if (row is null)
                {
                    continue;
                }

                Reveal(panel, section);
                ClearSelection();
                if (ReferenceEquals(panel, TodayTabPanel))
                {
                    OpenTodayRequested?.Invoke(this, EventArgs.Empty);
                    FocusTodayRowRequested?.Invoke(this, row.Key);
                    return null;
                }

                if (row.OwnerKey is TaskRowKey owner)
                {
                    if (!(IsDetailOpen && _selectedKey == owner))
                    {
                        OpenOwner(owner);
                    }
                }
                else
                {
                    SelectRow(row);
                }

                // From another page the window switches to Tasks first; the row takes the keyboard once the page shows it.
                OpenRequested?.Invoke(this, EventArgs.Empty);
                FocusRowRequested?.Invoke(this, row.Key);
                return null;
            }
        }

        return text + " isn't shown in the current view";
    }

    // The Shelf shows (hidden during a session: Ctrl+G shows it, as Show the Shelf does), and the section is unfolded.
    private void Reveal(TaskSectionsPanelViewModel panel, TaskSectionViewModel section)
    {
        if (ReferenceEquals(panel, Shelf.Panel) && _shelf.Show())
        {
            ShelfVisibilityChanged();
        }

        if (!section.IsOpen)
        {
            panel.Toggle(section);
        }
    }
}
