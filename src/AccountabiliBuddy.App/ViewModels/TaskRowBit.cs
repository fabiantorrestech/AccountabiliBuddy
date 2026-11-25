using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// One piece of a task row's detail line (spec 03 §B, prototype <c>bits</c>): an optional icon and its words, e.g. ⏱ "50 min",
/// 📅 "Fri 11/13", 🔔 "10 min before", a "#tag" chip, "untouched 5 wk", the origin. A record, so a rebuilt row compares by value.
/// </summary>
/// <param name="Text">The words; empty for an icon on its own (the sync-problem cloud).</param>
/// <param name="Icon">The Material icon before the words; null for none.</param>
/// <param name="Tip">The tooltip, e.g. "Time limit"; empty for none.</param>
/// <param name="IsChip">Drawn as a small Surface2 pill (tags).</param>
/// <param name="OpensConflict">The sync-problem cloud: a click opens "Changed on both devices".</param>
/// <param name="Tag">A tag on the Tasks page (Tasks redesign 4c): drawn in the accent colour; a click searches it. Null for other bits.</param>
/// <param name="Dot">A label's 7 px dot before the words (a Shelf row's sub-line); null for none.</param>
/// <param name="AccentIcon">The icon in the accent colour, 12 px (the bin on a Group by Labels / None row's sub-line).</param>
public sealed record TaskRowBit(
    string Text, MaterialIconKind? Icon = null, string Tip = "", bool IsChip = false, TaskRowBitTone Tone = TaskRowBitTone.Muted, bool OpensConflict = false,
    string? Tag = null, Avalonia.Media.IBrush? Dot = null, bool AccentIcon = false)
{
    public bool HasDot => Dot is not null;

    /// <summary>The icon's size: 12 for the accent bin icon, else 13.</summary>
    public double IconSize => AccentIcon ? 12 : 13;

    public bool IsPlain => !OpensConflict && !IsTag;

    public bool IsTag => Tag is not null;

    public bool HasIcon => Icon is not null;

    public MaterialIconKind IconKind => Icon ?? MaterialIconKind.CircleSmall;

    public bool HasText => Text.Length > 0;

    public bool HasTip => Tip.Length > 0;

    public bool IsInk => Tone == TaskRowBitTone.Ink;

    public bool IsFaint => Tone == TaskRowBitTone.Faint;

    public bool IsDrift => Tone == TaskRowBitTone.Drift;

    public bool IsAmber => Tone == TaskRowBitTone.Amber;
}
