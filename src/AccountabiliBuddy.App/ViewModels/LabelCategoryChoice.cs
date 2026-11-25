using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// One entry of the Labels screen's new-label category choice (ADR 93): a category with its icon, or "Other labels" (<see cref="Id"/> null:
/// the new label has no category).
/// </summary>
public sealed record LabelCategoryChoice(Guid? Id, string Name, MaterialIconKind Icon)
{
    public override string ToString() => Name;
}
