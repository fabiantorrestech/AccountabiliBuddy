using System.Windows.Input;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// One chip of the Goals page's area row (boards Goals-PC, Goals-PC-Small: "+ · All · Work · Fitness · Learning · Personal"): "All" (no
/// area, no icon) or an area with its icon. The picked one is drawn selected; picking shows only that area's goals, for this visit.
/// </summary>
public sealed record GoalAreaChipViewModel(Guid? AreaId, string Text, MaterialIconKind? Icon, bool IsSelected, ICommand PickCommand)
{
    public bool HasIcon => Icon is not null;

    public MaterialIconKind IconOrDefault => Icon ?? MaterialIconKind.ShapeOutline;
}
