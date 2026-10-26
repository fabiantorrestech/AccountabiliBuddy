using AccountabiliBuddy.Core.Ports;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;

namespace AccountabiliBuddy.App.Views;

/// <summary>
/// The on-screen window's own hint (see <see cref="OverlayHint"/>): a small always-on-top window next to the cursor that
/// clicks pass through and that never takes focus. Kept on the cursor's screen; below and right of the cursor, or above
/// it near the bottom edge.
/// </summary>
public partial class OverlayHintWindow : Window
{
    // From the cursor to the hint's top-left corner (display pixels).
    private const double OffsetXDips = 16;
    private const double OffsetYDips = 20;

    private readonly IOverlayWindowControl? _control;
    private PixelPoint _cursor;

    // Used by the XAML previewer.
    public OverlayHintWindow()
        : this(null)
    {
    }

    public OverlayHintWindow(IOverlayWindowControl? control)
    {
        _control = control;
        InitializeComponent();

        Opened += (_, _) =>
        {
            if (_control is not null && TryGetPlatformHandle() is IPlatformHandle handle)
            {
                _control.SetClickThrough(handle.Handle, true);
            }

            Place();
        };
        // The size is known only after layout; place it again so it stays on the screen.
        SizeChanged += (_, _) => Place();
    }

    /// <summary>Shows <paramref name="text"/> next to the cursor at <paramref name="screenPoint"/> (physical pixels), or moves it there.</summary>
    public void ShowAt(string text, PixelPoint screenPoint)
    {
        HintText.Text = text;
        _cursor = screenPoint;
        Place();
        if (!IsVisible)
        {
            Show();
            KeepOnTop();
        }
    }

    /// <summary>Back in front of the on-screen window, which is put on top every tick; only while showing.</summary>
    public void KeepOnTop()
    {
        if (IsVisible && _control is not null && TryGetPlatformHandle() is IPlatformHandle handle)
        {
            _control.KeepOnTop(handle.Handle);
        }
    }

    private void Place()
    {
        Screen? screen = Screens.ScreenFromPoint(_cursor) ?? Screens.Primary;
        double scaling = screen?.Scaling ?? 1;
        int width = (int)Math.Ceiling(Bounds.Width * scaling);
        int height = (int)Math.Ceiling(Bounds.Height * scaling);
        int x = _cursor.X + (int)Math.Round(OffsetXDips * scaling);
        int y = _cursor.Y + (int)Math.Round(OffsetYDips * scaling);
        if (screen is not null)
        {
            PixelRect area = screen.Bounds;
            if (x + width > area.Right)
            {
                x = Math.Max(area.X, area.Right - width);
            }

            if (y + height > area.Bottom)
            {
                // Near the bottom edge: above the cursor instead.
                y = Math.Max(area.Y, _cursor.Y - height - (int)Math.Round(OffsetYDips * scaling / 2));
            }
        }

        Position = new PixelPoint(x, y);
    }
}
