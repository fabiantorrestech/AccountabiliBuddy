using System.Collections.ObjectModel;
using AccountabiliBuddy.App.Services.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The sections of one task panel (the Tasks page's Today column "today", the Today page's "todaytab", the Shelf "shelf") and how
/// they fold (shared behaviour §1): a section is open when the user unfolded it since Tasks was last opened, else when it is pinned
/// open, else as the panel's last Expand all / Collapse all left it, else by its own default (This session open, the quieter sections folded, the Shelf's open).
/// Expand all / Collapse all (the unfold button, or a header's right-click) are remembered per panel; the pins are shared by both
/// Today panels; both live in <see cref="TaskLayoutStore"/>.
/// </summary>
public sealed partial class TaskSectionsPanelViewModel : ObservableObject
{
    private readonly TaskLayoutStore _layout;
    private readonly Dictionary<string, TaskSectionViewModel> _known = [];
    private readonly Dictionary<string, bool> _folds = [];

    public TaskSectionsPanelViewModel(string key, TaskLayoutStore layout)
    {
        Key = key;
        _layout = layout;
        _layout.Changed += (_, _) => Apply();
    }

    /// <summary>"today", "todaytab" or "shelf".</summary>
    public string Key { get; }

    public ObservableCollection<TaskSectionViewModel> Sections { get; } = new();

    /// <summary>The panel's last Expand all / Collapse all was Expand all: the unfold button offers Collapse all.</summary>
    public bool IsExpanded => _layout.Current.ExpandAllOf(Key) == true;

    public MaterialIconKind ExpandIcon => IsExpanded ? MaterialIconKind.UnfoldLessHorizontal : MaterialIconKind.UnfoldMoreHorizontal;

    public string ExpandTip => IsExpanded ? "Collapse all (pinned sections stay open)" : "Expand all";

    /// <summary>A session runs: the quiet sections fade further (0.40).</summary>
    public bool InSession
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                Apply();
            }
        }
    }

    /// <summary>
    /// The section with this key, made once and kept (so its fold and its add box survive a rebuild): <paramref name="quiet"/> fades
    /// it, <paramref name="canPin"/> lets it be pinned open, <paramref name="opensByDefault"/> is its fold before any choice.
    /// </summary>
    public TaskSectionViewModel Section(string key, ObservableCollection<TaskListRowViewModel> rows, bool quiet = false, bool canPin = false, bool opensByDefault = true)
    {
        if (!_known.TryGetValue(key, out TaskSectionViewModel? section) || !ReferenceEquals(section.Rows, rows))
        {
            section = new TaskSectionViewModel(this, key, rows) { IsQuiet = quiet, CanPin = canPin, OpensByDefault = opensByDefault };
            _known[key] = section;
        }

        return section;
    }

    /// <summary>Shows <paramref name="wanted"/> in order, moving the sections already shown rather than drawing them again.</summary>
    public void Show(IReadOnlyList<TaskSectionViewModel> wanted)
    {
        for (int index = 0; index < wanted.Count; index++)
        {
            TaskSectionViewModel section = wanted[index];
            if (index < Sections.Count && ReferenceEquals(Sections[index], section))
            {
                continue;
            }

            int at = Sections.IndexOf(section);
            if (at > index)
            {
                Sections.Move(at, index);
            }
            else
            {
                Sections.Insert(index, section);
            }
        }

        while (Sections.Count > wanted.Count)
        {
            Sections.RemoveAt(Sections.Count - 1);
        }

        Apply();
    }

    /// <summary>A header click: folds or unfolds it until Tasks is next opened.</summary>
    public void Toggle(TaskSectionViewModel section)
    {
        _folds[section.Key] = !section.IsOpen;
        Apply();
    }

    /// <summary>Tasks was opened again: the header clicks are forgotten, the pins and Expand / Collapse all decide again.</summary>
    public void ForgetFolds()
    {
        _folds.Clear();
        Apply();
    }

    [RelayCommand]
    private Task ToggleExpandAsync() => IsExpanded ? CollapseAllAsync() : ExpandAllAsync();

    private Task ExpandAllAsync()
    {
        _folds.Clear();
        return _layout.SetExpandAllAsync(Key, true);
    }

    // Pinned sections stay open.
    private Task CollapseAllAsync()
    {
        _folds.Clear();
        return _layout.SetExpandAllAsync(Key, false);
    }

    /// <summary>A header's right-click: Expand all, Collapse all, then "Pinned open" with a switch per section that can be pinned.</summary>
    public IReadOnlyList<TaskMenuEntry> HeaderMenu()
    {
        var entries = new List<TaskMenuEntry>
        {
            TaskMenuEntry.Async("Expand all", ExpandAllAsync, MaterialIconKind.UnfoldMoreHorizontal),
            TaskMenuEntry.Async("Collapse all", CollapseAllAsync, MaterialIconKind.UnfoldLessHorizontal),
        };
        List<TaskSectionViewModel> pinnable = Sections.Where(section => section.CanPin).ToList();
        if (pinnable.Count > 0)
        {
            entries.Add(TaskMenuEntry.Separator);
            entries.Add(new TaskMenuEntry("Pinned open", IsHeading: true));
            foreach (TaskSectionViewModel section in pinnable)
            {
                string key = section.Key;
                entries.Add(new TaskMenuEntry(
                    section.Title, new AsyncRelayCommand(() => _layout.TogglePinAsync(key)), IsChecked: section.IsPinned,
                    Icon: section.IsPinned ? MaterialIconKind.Pin : MaterialIconKind.PinOutline));
            }
        }

        return entries;
    }

    // Every section's fold, pin, fade and density from the current state.
    private void Apply()
    {
        TaskLayoutPreferences layout = _layout.Current;
        bool? expandAll = layout.ExpandAllOf(Key);
        foreach (TaskSectionViewModel section in Sections)
        {
            section.IsPinned = section.CanPin && layout.PinnedNow.Contains(section.Key);
            section.IsOpen = _folds.TryGetValue(section.Key, out bool open) ? open : section.IsPinned || (expandAll ?? section.OpensByDefault);
            section.Fade = section.IsQuiet ? (InSession ? 0.40 : 0.66) : 1;
            section.IsCompact = layout.Compact;
        }

        OnPropertyChanged(nameof(IsExpanded));
        OnPropertyChanged(nameof(ExpandIcon));
        OnPropertyChanged(nameof(ExpandTip));
    }
}
