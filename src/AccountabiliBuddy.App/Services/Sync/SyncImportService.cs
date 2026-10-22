using AccountabiliBuddy.Core.Sync;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ZXing;
using ZXing.Common;
using ZXing.Multi.QrCode;

namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>
/// Sync files brought in by hand (ADR 22): a .absync file (opened, or dropped on the main window), or pictures of the
/// phone's QR codes for a PC without a camera. Codes from several pictures add up until the file is whole. Use from
/// the UI thread only.
/// </summary>
public sealed class SyncImportService
{
    // A sync file is a few KB; anything this big isn't one, and isn't read into memory (as the sync folder).
    private const long MaxFileBytes = 16 * 1024 * 1024;

    private readonly SyncCoordinator _coordinator;
    private readonly SyncIdentityService _identity;
    private readonly SyncQrCollector _codes = new();

    public SyncImportService(SyncCoordinator coordinator, SyncIdentityService identity)
    {
        _coordinator = coordinator;
        _identity = identity;
    }

    /// <summary>Takes in a sync file; returns what happened, e.g. "Synced Pixel 7" or why not.</summary>
    public async Task<string> ImportFileAsync(string path)
    {
        byte[] bytes;
        try
        {
            if (new FileInfo(path).Length > MaxFileBytes)
            {
                return "That file is too big to be a sync file.";
            }

            bytes = await File.ReadAllBytesAsync(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"Can't read that file ({ex.Message}).";
        }

        return await AcceptAsync(bytes, SyncDeviceCard.MethodFile);
    }

    /// <summary>
    /// Reads the sync QR codes in the pictures and adds them to the ones read before: "2 of 3 codes, add the rest"
    /// until the file is whole, then takes it in.
    /// </summary>
    public async Task<string> ImportPicturesAsync(IReadOnlyList<string> paths)
    {
        int found = 0;
        foreach (string path in paths)
        {
            IReadOnlyList<string> links;
            try
            {
                links = await Task.Run(() => ReadCodes(path));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                return $"Can't read that picture ({ex.Message}).";
            }

            foreach (string link in links)
            {
                found += _codes.Add(link) ? 1 : 0;
                if (_codes.Complete is byte[] whole)
                {
                    _codes.Reset();
                    return await AcceptAsync(whole, SyncDeviceCard.MethodQr);
                }
            }
        }

        if (found == 0)
        {
            return "No sync code found in that picture.";
        }

        return $"{_codes.Received} of {_codes.Total} codes, add the rest";
    }

    private async Task<string> AcceptAsync(byte[] sealedBytes, string method)
    {
        string? problem = await _coordinator.AcceptSealedAsync(sealedBytes, method);
        if (problem is not null)
        {
            return problem;
        }

        string? name = _identity.Key is SyncKey key ? SyncFileCodec.Open(sealedBytes, key)?.Device.Name : null;
        return $"Synced {name ?? "your other device"}";
    }

    // Every QR code in the picture that reads as text (the caller keeps the sync ones).
    private static IReadOnlyList<string> ReadCodes(string path)
    {
        int width;
        using (var probe = new Bitmap(path))
        {
            width = probe.PixelSize.Width;
        }

        // Decoded at its own width into a bitmap whose pixels can be read (a plain Bitmap can't copy them out).
        using FileStream stream = File.OpenRead(path);
        using WriteableBitmap decoded = WriteableBitmap.DecodeToWidth(stream, width, BitmapInterpolationMode.None);
        int height = decoded.PixelSize.Height;
        byte[] pixels;
        RGBLuminanceSource.BitmapFormat format;
        using (ILockedFramebuffer frame = decoded.Lock())
        {
            pixels = new byte[frame.RowBytes * height];
            System.Runtime.InteropServices.Marshal.Copy(frame.Address, pixels, 0, pixels.Length);
            format = frame.Format == PixelFormat.Rgba8888 ? RGBLuminanceSource.BitmapFormat.RGBA32 : RGBLuminanceSource.BitmapFormat.BGRA32;
            width = frame.RowBytes / 4;
        }

        var source = new RGBLuminanceSource(pixels, width, height, format);
        var hints = new Dictionary<DecodeHintType, object>
        {
            [DecodeHintType.TRY_HARDER] = true,
            [DecodeHintType.POSSIBLE_FORMATS] = new List<BarcodeFormat> { BarcodeFormat.QR_CODE },
        };
        Result[]? results = new QRCodeMultiReader().decodeMultiple(new BinaryBitmap(new HybridBinarizer(source)), hints);
        return results?.Select(result => result.Text).Where(text => !string.IsNullOrEmpty(text)).Distinct().ToList() ?? [];
    }
}
