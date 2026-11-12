using AccountabiliBuddy.Core.Tasks;

namespace AccountabiliBuddy.App.ViewModels;

// The keyboard map's page side (spec 03 §E, Implementation Notes 13k): which menu Del / F2 / H act through, Ctrl+G's go to a task by
// its ID, Ctrl+Z, and the page's part of Esc. The keys themselves are read by Services/TaskShortcuts (the window) and the rows.
public sealed partial class TasksViewModel
{
    /// <summary>Ctrl+G found a task: the page puts the keyboard on its row (after the page shows it).</summary>
    public event EventHandler<TaskRowKey>? FocusRowRequested;

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
    /// Ctrl+G: opens the task with this ID ("142", or "142b" for its step) on the Tasks page: the Shelf or Today column shows if it was
    /// hidden, its section unfolds, the task opens and its row takes the keyboard. Null when it opened; otherwise what to say ("No task
    /// 142"). Only tasks the page lists can open (the Shelf's view may leave one out: it says so).
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
        foreach (TaskSectionsPanelViewModel panel in new[] { TodayPanel, Shelf.Panel })
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

                FocusRowRequested?.Invoke(this, row.Key);
                return null;
            }
        }

        return text + " isn't shown in the current view";
    }

    // The column holding the section shows, and the section is unfolded.
    private void Reveal(TaskSectionsPanelViewModel panel, TaskSectionViewModel section)
    {
        if (ReferenceEquals(panel, Shelf.Panel) && !IsShelfShown)
        {
            ToggleShelfCommand.Execute(null);
        }

        if (ReferenceEquals(panel, TodayPanel) && IsTodayHidden)
        {
            IsTodayHidden = false;
        }

        if (!section.IsOpen)
        {
            panel.Toggle(section);
        }
    }
}
