using Avalonia.Media;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One fact chip of the tidy-up's task card: "● Work", "To sort", "Untouched 4 wk", "#ui", "25 min", "Effort not set".</summary>
/// <param name="Dot">The label's colour dot; null for none.</param>
/// <param name="Icon">An icon before the words; null for none.</param>
public sealed record TidyUpFact(string Text, IBrush? Dot = null, MaterialIconKind? Icon = null, bool IsFaint = false)
{
    public bool HasDot => Dot is not null;

    public bool HasIcon => Icon is not null;

    public MaterialIconKind IconKind => Icon ?? MaterialIconKind.CircleSmall;
}
