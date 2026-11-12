using Avalonia.Media;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// One choice in a goal dialog's picker (round 2 R5.4): an icon, a colour, an area, a label, a calendar category or a Beeminder goal.
/// <see cref="Key"/> is what is stored (an icon name, "#RRGGBB", an id as text, a slug); <see cref="Text"/> is what is shown.
/// </summary>
/// <param name="Note">A muted second line ("lb · 3 safe days").</param>
public sealed record GoalChoice(string Key, string Text, MaterialIconKind? Icon = null, IBrush? Brush = null, string? Note = null)
{
    public bool HasIcon => Icon is not null;

    public bool HasBrush => Brush is not null;

    public bool HasNote => !string.IsNullOrEmpty(Note);

    public override string ToString() => Text;
}
