namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>One sync method in use on the confirmation board, with a row per other device.</summary>
/// <param name="Method">A code of <see cref="Core.Sync.SyncReceiptParts.Methods"/>.</param>
/// <param name="Title">"Shared folder", "Google Drive", "Own relay", "QR codes", "File import".</param>
/// <param name="Devices">One row per other device in the group (empty while this PC is alone in it).</param>
public sealed record SyncBoardMethod(string Method, string Title, IReadOnlyList<SyncBoardDevice> Devices);
