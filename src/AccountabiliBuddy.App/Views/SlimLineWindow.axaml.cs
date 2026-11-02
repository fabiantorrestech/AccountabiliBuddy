using AccountabiliBuddy.App.ViewModels;
using AccountabiliBuddy.Core.Ports;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;

namespace AccountabiliBuddy.App.Views;

/// <summary>The slim line (15b): a click-through strip along the bottom of a monitor whose taskbar hides itself.</summary>
public partial class SlimLineWindow : Window
{
    private const double HeightDips = 26;

    private readonly IOverlayWindowControl _control;
    private readonly TaskbarOverlayViewModel _model;
    private PixelBox _monitor = new(0, 0, 0, 0);

    // Used by the XAML previewer.
    public SlimLineWindow()
        : this(null!, null!)
    {
    }

    public SlimLineWindow(IOverlayWindowControl control, TaskbarOverlayViewModel model)
    {
        _control = control;
        _model = model;
        InitializeComponent();
        DataContext = model;
        Opened += (_, _) =>
        {
            if (TryGetPlatformHandle() is IPlatformHandle handle)
            {
                _control.SetClickThrough(handle.Handle, true);
            }

            Reposition();
        };
        SizeChanged += (_, _) =>
        {
            // Windows may have made the window taller than asked (see Reposition), which only shows once it is sized.
            Reposition();
            UpdateLine();
        };
    }

    public void Apply(PixelBox monitor)
    {
        _monitor = monitor;
        if (!IsVisible)
        {
            Position = new PixelPoint(monitor.Left + 1, monitor.Bottom - 30);
        }

        Reposition();
        UpdateLine();
    }

    private void Reposition()
    {
        if (_monitor.Width == 0)
        {
            return;
        }

        double scale = DesktopScaling;
        Width = _monitor.Width / scale;
        Height = HeightDips;
        // Windows will not size a window below its minimum track height (about 56 px at 150%), so the real window can be taller than the 26 dips
        // asked for. The line and the minutes sit at the window's bottom edge, so what has to hold is that edge: place the window by the height it
        // really has, not the height it was asked for.
        double realDips = Math.Max(HeightDips, (FrameSize ?? ClientSize).Height);
        Position = new PixelPoint(_monitor.Left, _monitor.Bottom - (int)Math.Ceiling(realDips * scale));
        if (TryGetPlatformHandle() is IPlatformHandle handle)
        {
            _control.KeepOnTop(handle.Handle);
        }
    }

    /// <summary>The done part of the session along the line.</summary>
    public void UpdateLine()
    {
        double done = _model.IsSession ? 1 - _model.Fraction : 0;
        Done.Width = Math.Max(0, Bounds.Width * Math.Clamp(done, 0, 1));
    }
}
