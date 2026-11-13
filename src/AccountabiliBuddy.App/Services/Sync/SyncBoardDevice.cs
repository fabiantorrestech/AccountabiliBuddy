using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>One other device under a method on the confirmation board (SYNC1b).</summary>
/// <param name="Id">The device id (for the screen's own use; never exported).</param>
/// <param name="Name">The device's name (on screen only: the log and the export use <paramref name="Ordinal"/>).</param>
/// <param name="Ordinal">"PC 1", "Phone 1", "Watch 1": the name the log and the export use.</param>
/// <param name="Kind">The card's kind (pc, phone, watch).</param>
/// <param name="AppVersion">The version its card says.</param>
/// <param name="Dot">This method's dot for this device.</param>
/// <param name="ConfirmedFile">The time of this PC's file it last confirmed by this method; null when none.</param>
/// <param name="LastFileAt">When its newest file was written (any method); null when none was read.</param>
/// <param name="Parts">The items its last receipt covers, each with a dot (the same under every method).</param>
/// <param name="RefusedHere">Part codes this PC itself refused from the device's newest file it accepted ("Couldn't apply Day plan from ...").</param>
public sealed record SyncBoardDevice(
    Guid Id, string Name, string Ordinal, string Kind, string AppVersion, SyncDot Dot, DateTimeOffset? ConfirmedFile, DateTimeOffset? LastFileAt,
    IReadOnlyList<SyncBoardPart> Parts, IReadOnlyList<string>? RefusedHere = null);
