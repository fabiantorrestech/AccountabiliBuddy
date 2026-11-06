using System.Collections.ObjectModel;
using AccountabiliBuddy.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// Today's banner of waits running in the background (ADR 66): each with its countdown and Cancel. Refreshed every tick and
/// whenever a wait is armed, cancelled or applied; rows are kept and updated in place, so a click on Cancel isn't lost.
/// </summary>
public sealed partial class RunningWaitsViewModel : ObservableObject
{
    private readonly RunningWaitsService _waits;

    public RunningWaitsViewModel(RunningWaitsService waits, SessionController sessions)
    {
        _waits = waits;
        sessions.Ticked += (_, _) => Refresh();
        waits.Changed += (_, _) => Refresh();
        Refresh();
    }

    public ObservableCollection<RunningWaitRowViewModel> Rows { get; } = [];

    [ObservableProperty]
    public partial bool HasWaits { get; set; }

    public void Refresh()
    {
        IReadOnlyList<RunningWait> current = _waits.Current();
        for (int index = Rows.Count - 1; index >= 0; index--)
        {
            if (current.All(wait => wait.Key != Rows[index].Wait.Key))
            {
                Rows.RemoveAt(index);
            }
        }

        foreach (RunningWait wait in current)
        {
            RunningWaitRowViewModel? row = Rows.FirstOrDefault(existing => existing.Wait.Key == wait.Key);
            if (row is null)
            {
                Rows.Add(new RunningWaitRowViewModel(wait, _waits.Describe(wait)));
            }
            else
            {
                row.Update(wait, _waits.Describe(wait));
            }
        }

        HasWaits = Rows.Count > 0;
    }
}
