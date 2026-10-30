using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.App.ViewModels;
using AccountabiliBuddy.Core.Ports;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Platform;

namespace AccountabiliBuddy.App.Views;

/// <summary>
/// The Ctrl+Alt panel (10c, 14b): a flyout window that rises from the overlay toward the middle of the screen, like a Windows flyout. A peek
/// (Ctrl+Alt held) never takes the keyboard focus; the sticky form (Ctrl+Alt+Space) does, so it takes keys itself, and closes on Esc or when the
/// user clicks anywhere else (it is deactivated). Rows, buttons and keys work like a menu: hover, ↑↓ and Enter.
/// </summary>
public partial class OverlayPanelWindow : Window
{
    private const int GapDips = 6;

    private readonly IOverlayWindowControl _control;
    private readonly OverlayPanelViewModel _model;
    private PixelRect _anchor;
    private OverlayTarget _target = OverlayTarget.Hidden;

    // Used by the XAML previewer.
    public OverlayPanelWindow()
        : this(null!, null!)
    {
    }

    public OverlayPanelWindow(IOverlayWindowControl control, OverlayPanelViewModel model)
    {
        _control = control;
        _model = model;
        InitializeComponent();
        DataContext = model;
        model.CloseRequested += (_, _) => Close();
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(OverlayPanelViewModel.Size))
            {
                ChooseRowTemplate();
            }
        };
        ChooseRowTemplate();
        SizeChanged += (_, _) => Reposition();
        Deactivated += (_, _) =>
        {
            if (IsSticky)
            {
                Close();
            }
        };
    }

    /// <summary>The panel stays until Esc or a click elsewhere (Ctrl+Alt+Space), instead of closing when Ctrl+Alt is let go.</summary>
    public bool IsSticky { get; private set; }

    /// <summary>Shows the panel above (or beside) the overlay's tile; calling it again moves and re-reads it.</summary>
    public void Present(PixelRect tile, OverlayTarget target, bool sticky)
    {
        _anchor = tile;
        _target = target;
        IsSticky = sticky;
        _model.Refresh();
        if (!IsVisible)
        {
            Position = new PixelPoint(tile.X, tile.Y - 1);
            Show();
        }

        if (TryGetPlatformHandle() is IPlatformHandle handle)
        {
            _control.SetActivatable(handle.Handle, sticky);
            _control.KeepOnTop(handle.Handle);
        }

        Reposition();
        if (sticky)
        {
            Activate();
        }
    }

    /// <summary>Back in front of the taskbar (which comes to the front when it is clicked).</summary>
    public void KeepOnTop()
    {
        if (TryGetPlatformHandle() is IPlatformHandle handle)
        {
            _control.KeepOnTop(handle.Handle);
        }
    }

    private void ChooseRowTemplate()
    {
        TaskList.ItemTemplate = (IDataTemplate)Resources[_model.ShowsSteps ? "taskFull" : "taskCompact"]!;
    }

    private void Reposition()
    {
        if (_anchor.Width == 0 || Bounds.Width <= 0)
        {
            return;
        }

        double scale = DesktopScaling;
        int width = (int)Math.Ceiling(Bounds.Width * scale);
        int height = (int)Math.Ceiling(Bounds.Height * scale);
        int gap = (int)(GapDips * scale);
        PixelRect area = (Screens.ScreenFromPoint(_anchor.Center) ?? Screens.Primary ?? Screens.All.First()).WorkingArea;
        if (_target.Kind == OverlayTargetKind.Docked || _target.Kind == OverlayTargetKind.Floating)
        {
            // Rises from the tile: its right edge lines up with the tile's when the tile hugs the right, else its left edge.
            int x = _target.AlignRight ? _anchor.Right - width : _anchor.X;
            int y = _anchor.Y - gap - height;
            Position = new PixelPoint(Math.Clamp(x, area.X, Math.Max(area.X, area.Right - width)), Math.Max(area.Y, y));
        }
        else
        {
            // Beside the vertical tile, toward the middle of the screen.
            bool tileOnRight = _anchor.Center.X > area.Center.X;
            int x = tileOnRight ? _anchor.X - gap - width : _anchor.Right + gap;
            int y = Math.Clamp(_anchor.Center.Y - (height / 2), area.Y, Math.Max(area.Y, area.Bottom - height));
            Position = new PixelPoint(Math.Clamp(x, area.X, Math.Max(area.X, area.Right - width)), y);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
            return;
        }

        string? key = e.Key switch
        {
            >= Key.D0 and <= Key.D9 => ((int)e.Key - (int)Key.D0).ToString(),
            >= Key.NumPad0 and <= Key.NumPad9 => ((int)e.Key - (int)Key.NumPad0).ToString(),
            Key.Up => "UP",
            Key.Down => "DOWN",
            Key.Enter or Key.Return => "ENTER",
            Key.OemPlus or Key.Add => "+",
            Key.OemMinus or Key.Subtract => "-",
            >= Key.A and <= Key.Z => e.Key.ToString(),
            _ => null,
        };
        if (key is not null && _model.HandleKey(key))
        {
            e.Handled = true;
        }
    }
}
