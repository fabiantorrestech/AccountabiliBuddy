using System.Collections.ObjectModel;
using AccountabiliBuddy.App.Services.Goals;
using AccountabiliBuddy.Core.Goals;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// Goals Options › Areas (round 2 R5.4, change note §4a "Reorder and edit areas in Options › List › Areas"; also Settings › Goals › Areas):
/// every area in order with its goal count, ↑ / ↓ to reorder, ✎ to rename or pick its icon and colour (Edit area), and "+ Add an area".
/// Changes apply at once and the list follows the store. Area names are private text: on screen only.
/// </summary>
public sealed partial class GoalAreasViewModel : IDisposable
{
    private readonly GoalsService _goals;

    public GoalAreasViewModel(GoalsService goals)
    {
        _goals = goals;
        _goals.Store.Changed += OnStoreChanged;
        Reload();
    }

    /// <summary>✎ or + Add an area: the view opens Edit area (null = a new area), then calls <see cref="Reload"/>.</summary>
    public event EventHandler<Guid?>? EditAreaRequested;

    public ObservableCollection<GoalAreaRowViewModel> Rows { get; } = [];

    public void Reload()
    {
        Rows.Clear();
        List<GoalArea> areas = _goals.Lists.Areas.Where(area => area.IsLive).OrderBy(area => area.Position).ToList();
        for (int i = 0; i < areas.Count; i++)
        {
            GoalArea area = areas[i];
            int count = _goals.Lists.LiveGoals.Count(goal => goal.AreaId == area.Id);
            Rows.Add(new GoalAreaRowViewModel(area.Id, area.Name, GoalIcons.Of(area.Icon), GoalIcons.Brush(area.Color), count, i == 0, i == areas.Count - 1, MoveAsync,
                id => EditAreaRequested?.Invoke(this, id)));
        }
    }

    public void Dispose()
    {
        _goals.Store.Changed -= OnStoreChanged;
    }

    [RelayCommand]
    private void AddArea()
    {
        EditAreaRequested?.Invoke(this, null);
    }

    private Task MoveAsync(GoalAreaRowViewModel row, int delta)
    {
        int at = Rows.IndexOf(row);
        return at < 0 ? Task.CompletedTask : GoalAreaOrder.MoveAsync(_goals, row.Id, at + delta, this);
    }

    private void OnStoreChanged(object? sender, GoalStoreChangedEventArgs e)
    {
        Reload();
    }
}
