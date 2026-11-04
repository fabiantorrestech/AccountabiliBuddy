using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.Core.Domain;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// One of tomorrow's top tasks on the routine's planning step: a task typed or chosen from the lists ("From your lists"), and the day
/// part of tomorrow's plan it is put in (the same "Put in plan" as the Tasks page: it joins the next session's list when the part starts).
/// </summary>
public sealed partial class ShutdownPlanRowViewModel : ObservableObject
{
    private string? _sourceTitle;

    public ShutdownPlanRowViewModel(SessionTasksService tasks, IReadOnlyList<ShutdownPartChoice> parts, int defaultPartIndex)
    {
        Parts = parts;
        PartIndex = defaultPartIndex;
        Picker = new ListPickerViewModel(tasks, tasks.Clock, FillAsync);
        Text = string.Empty;
    }

    /// <summary>Tomorrow's day parts, earliest first.</summary>
    public IReadOnlyList<ShutdownPartChoice> Parts { get; }

    /// <summary>The part picked in the box (-1: none).</summary>
    [ObservableProperty]
    public partial int PartIndex { get; set; }

    [ObservableProperty]
    public partial string Text { get; set; }

    public ListPickerViewModel Picker { get; }

    /// <summary>The list task chosen: it is planned where it is and leaves its list. Null for a typed one; changing the text makes it typed.</summary>
    public (PulledListKind Kind, Guid Id)? Source { get; private set; }

    /// <summary>The task as typed or chosen, trimmed; empty = this row plans nothing.</summary>
    public string Title => Text.Trim();

    /// <summary>The part it goes in; null when none is picked.</summary>
    public PlannedSlot? Slot => PartIndex >= 0 && PartIndex < Parts.Count ? Parts[PartIndex].Slot : null;

    partial void OnTextChanged(string value)
    {
        if (Source is not null && value.Trim() != _sourceTitle)
        {
            Source = null;
            _sourceTitle = null;
        }
    }

    private Task FillAsync(PulledListKind kind, Guid id, string title)
    {
        _sourceTitle = title.Trim();
        Picker.WillFill(title);
        Text = title;
        Source = (kind, id);
        return Task.CompletedTask;
    }
}
