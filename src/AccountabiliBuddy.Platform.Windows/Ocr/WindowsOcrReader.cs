using System.Runtime.InteropServices.WindowsRuntime;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Ports;
using AccountabiliBuddy.Platform.Windows.Activity;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Win32.Foundation;

namespace AccountabiliBuddy.Platform.Windows.Ocr;

/// <summary>
/// Reads on-screen text with the OCR engine built into Windows (Windows.Media.Ocr), in the user's display
/// languages. Runs entirely on this computer; images and text are kept in memory only.
/// </summary>
public sealed class WindowsOcrReader : IScreenTextReader
{
    private readonly OcrEngine? _engine = OcrEngine.TryCreateFromUserProfileLanguages();

    /// <summary>False when Windows has no OCR language pack for any of the user's languages.</summary>
    public bool IsAvailable => _engine is not null;

    public async Task<string?> ReadTextAsync(ActivitySnapshot snapshot, CancellationToken cancellationToken = default)
    {
        if (_engine is null || !WindowIds.TryParse(snapshot.WindowId, out HWND window))
        {
            return null;
        }

        ScreenImage? image = WindowCapture.Capture(window);
        if (image is null)
        {
            return null;
        }

        image = FitWithin(image, (int)OcrEngine.MaxImageDimension);
        cancellationToken.ThrowIfCancellationRequested();

        using SoftwareBitmap bitmap = SoftwareBitmap.CreateCopyFromBuffer(
            image.Bgra.AsBuffer(), BitmapPixelFormat.Bgra8, image.Width, image.Height, BitmapAlphaMode.Premultiplied);
        OcrResult result = await _engine.RecognizeAsync(bitmap).AsTask(cancellationToken).ConfigureAwait(false);

        return string.Join("\n", result.Lines.Select(line => line.Text));
    }

    /// <summary>Halves the image until both sides fit the OCR engine's limit (rare: only for very large windows).</summary>
    private static ScreenImage FitWithin(ScreenImage image, int maxDimension)
    {
        while (image.Width > maxDimension || image.Height > maxDimension)
        {
            int width = image.Width / 2;
            int height = image.Height / 2;
            byte[] half = new byte[width * height * 4];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int source = (((y * 2) * image.Width) + (x * 2)) * 4;
                    int target = ((y * width) + x) * 4;
                    half[target] = image.Bgra[source];
                    half[target + 1] = image.Bgra[source + 1];
                    half[target + 2] = image.Bgra[source + 2];
                    half[target + 3] = 255;
                }
            }

            image = new ScreenImage(width, height, half);
        }

        return image;
    }
}
