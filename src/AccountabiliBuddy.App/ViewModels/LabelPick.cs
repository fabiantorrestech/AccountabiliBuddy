namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// What a <see cref="LabelPickerViewModel"/> holds: nothing picked yet (keep each task's label), "No label" (take the label off), or one label.
/// </summary>
/// <param name="LabelId">The picked label; null for nothing or for "No label" (<paramref name="IsNoLabel"/>).</param>
/// <param name="IsNoLabel">"No label" was picked.</param>
public readonly record struct LabelPick(Guid? LabelId, bool IsNoLabel)
{
    /// <summary>Nothing picked: the tasks keep their labels.</summary>
    public static LabelPick Nothing => default;

    /// <summary>"No label": the tasks lose their label.</summary>
    public static LabelPick NoLabel => new(null, true);

    public bool IsNothing => LabelId is null && !IsNoLabel;

    public static LabelPick Of(Guid labelId) => new(labelId, false);
}
