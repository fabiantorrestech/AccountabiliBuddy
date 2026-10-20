using AccountabiliBuddy.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One row of the timeline in Settings → Day bar: shown or not, and moved up or down the list.</summary>
public sealed partial class TimelineRowSettingViewModel : ObservableObject
{
    private readonly Action<TimelineRowSettingViewModel, int> _move;

    /// <param name="move">Moves this row by the given step (-1 = up, +1 = down) in the parent's list.</param>
    public TimelineRowSettingViewModel(TimelineRow row, bool shown, Action<TimelineRowSettingViewModel, int> move)
    {
        Row = row;
        Name = NameOf(row);
        Shown = shown;
        _move = move;
    }

    public TimelineRow Row { get; }

    public string Name { get; }

    [ObservableProperty]
    public partial bool Shown { get; set; }

    public TimelineRowChoice ToChoice()
    {
        return new TimelineRowChoice(Row, Shown);
    }

    [RelayCommand]
    private void MoveUp()
    {
        _move(this, -1);
    }

    [RelayCommand]
    private void MoveDown()
    {
        _move(this, 1);
    }

    private static string NameOf(TimelineRow row)
    {
        return row switch
        {
            TimelineRow.Calendar => "Calendar (events)",
            TimelineRow.Plan => "Plan (your day plan)",
            TimelineRow.Focus => "Focus (sessions you started, drift marks)",
            TimelineRow.Blockers => "Blockers (Cold Turkey's blocks)",
            _ => row.ToString(),
        };
    }
}
