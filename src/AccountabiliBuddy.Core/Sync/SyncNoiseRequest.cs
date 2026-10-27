namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// The <see cref="SyncAction.Text"/> of a <see cref="SyncAction.KindNoise"/> action (ADR 42), as JSON:
/// <c>{ "op": "toggle" | "next" | "prev" | "volume", "value": 0.5 }</c>.
/// </summary>
/// <param name="Op">One of the <c>Op…</c> constants.</param>
/// <param name="Value">0.0 to 1.0, for <see cref="OpVolume"/> only.</param>
public sealed record SyncNoiseRequest(string Op, double? Value = null)
{
    public const string OpToggle = "toggle";
    public const string OpNext = "next";
    public const string OpPrev = "prev";
    public const string OpVolume = "volume";
}
