using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>One item (a part of the sync file) under a device on the confirmation board: its code and its dot.</summary>
/// <param name="Code">A code of <see cref="SyncReceiptParts.Codes"/>.</param>
/// <param name="Dot">Green when taken from my newest file, yellow while waiting or held, red when refused or not confirmed, grey when off or not carried.</param>
public sealed record SyncBoardPart(string Code, SyncDot Dot);
