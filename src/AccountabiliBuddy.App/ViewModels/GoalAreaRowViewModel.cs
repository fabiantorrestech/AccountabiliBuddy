using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One row of Goals Options › Areas (round 2 R5.4): the area's icon and name, how many goals it holds, ↑ / ↓ and ✎ (Edit area).</summary>
public sealed partial class GoalAreaRowViewModel
{
    private readonly Func<GoalAreaRowViewModel, int, Task> _move;
    private readonly Action<Guid> _edit;

    public GoalAreaRowViewModel(Guid id, string name, MaterialIconKind icon, IBrush brush, int goals, bool isFirst, bool isLast, Func<GoalAreaRowViewModel, int, Task> move, Action<Guid> edit)
    {
        Id = id;
        Name = name;
        Icon = icon;
        Brush = brush;
        CountText = goals == 1 ? "1 goal" : $"{goals} goals";
        CanMoveUp = !isFirst;
        CanMoveDown = !isLast;
        _move = move;
        _edit = edit;
    }

    public Guid Id { get; }

    public string Name { get; }

    public MaterialIconKind Icon { get; }

    public IBrush Brush { get; }

    public string CountText { get; }

    public bool CanMoveUp { get; }

    public bool CanMoveDown { get; }

    [RelayCommand]
    private Task MoveUp() => CanMoveUp ? _move(this, -1) : Task.CompletedTask;

    [RelayCommand]
    private Task MoveDown() => CanMoveDown ? _move(this, 1) : Task.CompletedTask;

    [RelayCommand]
    private void Edit() => _edit(Id);
}
