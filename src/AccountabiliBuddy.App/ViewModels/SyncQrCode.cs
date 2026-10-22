using Avalonia.Media.Imaging;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One of the sync QR codes in <see cref="SyncQrViewModel"/>, e.g. "2 of 3".</summary>
public sealed record SyncQrCode(Bitmap Picture, string Label);
