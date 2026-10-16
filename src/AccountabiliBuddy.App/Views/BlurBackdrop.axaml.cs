using System.Runtime.InteropServices;
using AccountabiliBuddy.Core.Ports;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace AccountabiliBuddy.App.Views;

public partial class BlurBackdrop : Window
{
    public BlurBackdrop()
    {
        InitializeComponent();
        Opened += (_, _) => Root.Opacity = 1;
    }

    /// <summary>Raised when the user clicks the backdrop (outside the popup).</summary>
    public event EventHandler? Dismissed;

    /// <summary>Shows <paramref name="image"/> blurred; without a snapshot the backdrop is a plain dim.</summary>
    public void SetSnapshot(ScreenImage? image)
    {
        if (image is null)
        {
            return;
        }

        var bitmap = new WriteableBitmap(
            new PixelSize(image.Width, image.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        using (ILockedFramebuffer buffer = bitmap.Lock())
        {
            int rowBytes = image.Width * 4;
            for (int row = 0; row < image.Height; row++)
            {
                Marshal.Copy(image.Bgra, row * rowBytes, buffer.Address + (row * buffer.RowBytes), rowBytes);
            }
        }

        Snapshot.Source = bitmap;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Dismissed?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        (Snapshot.Source as IDisposable)?.Dispose();
    }
}
