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
public sealed record TaskRowBit(
    string Text, MaterialIconKind? Icon = null, string Tip = "", bool IsChip = false, TaskRowBitTone Tone = TaskRowBitTone.Muted, bool OpensConflict = false)
{
    public bool IsPlain => !OpensConflict;

    public bool HasIcon => Icon is not null;

    public MaterialIconKind IconKind => Icon ?? MaterialIconKind.CircleSmall;

    public bool HasText => Text.Length > 0;

    public bool HasTip => Tip.Length > 0;

    public bool IsInk => Tone == TaskRowBitTone.Ink;

    public bool IsFaint => Tone == TaskRowBitTone.Faint;

    public bool IsDrift => Tone == TaskRowBitTone.Drift;
}
