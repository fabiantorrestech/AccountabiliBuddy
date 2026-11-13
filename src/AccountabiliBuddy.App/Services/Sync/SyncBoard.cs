namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>
/// The per-method, per-device, per-item confirmation board (SYNC1b, ADR 86), built by <see cref="SyncBoardBuilder"/>.
/// </summary>
/// <param name="Methods">The methods in use, in the order folder, Drive, relay, QR, file; empty when none is on (works alone: "Not syncing").</param>
/// <param name="Newest">When this PC's newest file was written (put anywhere); null when it has put none yet.</param>
public sealed record SyncBoard(IReadOnlyList<SyncBoardMethod> Methods, DateTimeOffset? Newest)
{
    /// <summary>No method is on: nothing to confirm, no warning.</summary>
    public bool NotSyncing => Methods.Count == 0;

    /// <summary>Methods are on but no other device has been heard from.</summary>
    public bool NoOtherDevices => Methods.Count > 0 && Methods.All(method => method.Devices.Count == 0);
}
