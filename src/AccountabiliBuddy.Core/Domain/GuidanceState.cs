namespace AccountabiliBuddy.Core.Domain;

/// <summary>
/// The guidance mode on one device (ADR 68), app-owned (PC document "guidance", phone preferences "guidance"), never in a profile.
/// </summary>
/// <param name="Mode">The mode in effect.</param>
/// <param name="EditedAt">
/// When the mode in effect was chosen, on any device. Null for a fresh install's ProductaBuddy, which nobody chose: never sent, and any
/// received mode replaces it. <see cref="UpgradeStamp"/> for an upgraded install's AccountabiliBuddy: sent, so a fresh device joining
/// follows a group that was guarding all along, and older than any real choice, so every choice beats it.
/// </param>
/// <param name="Pending">A switch to Companion waiting out its delay; null when none waits.</param>
public sealed record GuidanceState(GuidanceMode Mode = GuidanceMode.Guarding, DateTimeOffset? EditedAt = null, GuidancePending? Pending = null)
{
    /// <summary>The edit time of an upgraded install's AccountabiliBuddy: before any real choice.</summary>
    public static DateTimeOffset UpgradeStamp { get; } = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>An install that predates the mode: guarding, as it always was.</summary>
    public static GuidanceState Upgraded { get; } = new(GuidanceMode.Guarding, UpgradeStamp);

    /// <summary>The mode in effect was chosen by someone (here or on another device), not a starting default.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsChosen => EditedAt is DateTimeOffset at && at > UpgradeStamp;

    /// <summary>A fresh install's default (ADR 68): ProductaBuddy, not yet chosen.</summary>
    public static GuidanceState FreshInstall { get; } = new(GuidanceMode.Companion);
}
