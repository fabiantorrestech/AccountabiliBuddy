using System.ComponentModel;
using AccountabiliBuddy.App.ViewModels;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace AccountabiliBuddy.App.Views;

/// <summary>
/// The title bar's session control. The running pill opens its extras (Step away, the end time, the session menu with End) while
/// the pointer is on it, while the keyboard focus is in it (Tab, not a click), while its menu is open, and always while away
/// (I'm back). Motion "Off": it opens at once and the ended target doesn't fade.
/// </summary>
public partial class SessionControlView : UserControl
{
    private const string OpenClass = "open";
    private const string FadeClass = "fade";

    private Transitions? _extrasTransitions;
    private SessionControlViewModel? _viewModel;
    private bool _pointerOver;
    private bool _keyboardFocus;
    private bool _menuOpen;

    public SessionControlView()
    {
        InitializeComponent();
        Pill.PointerEntered += (_, _) =>
        {
            _pointerOver = true;
            UpdateOpen();
        };
        Pill.PointerExited += (_, _) =>
        {
            _pointerOver = false;
            UpdateOpen();
        };
        Pill.AddHandler(GotFocusEvent, OnPillGotFocus);
        Pill.AddHandler(LostFocusEvent, OnPillLostFocus);
        if (MoreButton.Flyout is { } menu)
        {
            menu.Opened += (_, _) =>
            {
                _menuOpen = true;
                UpdateOpen();
            };
            menu.Closed += (_, _) =>
            {
                _menuOpen = false;
                UpdateOpen();
            };
        }
    }

    /// <summary>The ⌄ session menu, for the title bar's one-pop-up-at-a-time rule.</summary>
    public Avalonia.Controls.Primitives.FlyoutBase? SessionMenu => MoreButton.Flyout;

    // Round 2 (brief §3.2, CN §1 rule 2): the control is centred in the title bar by its resting width; the extras (Step away, ⌄,
    // the end time) open to the left over the empty title bar, so the ring and the time never move when they open.
    protected override Size MeasureOverride(Size availableSize)
    {
        Size full = base.MeasureOverride(availableSize);
        return new Size(Math.Max(0, full.Width - ExtrasWidth()), full.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double extras = ExtrasWidth();
        foreach (Visual child in VisualChildren)
        {
            if (child is Layoutable layoutable)
            {
                layoutable.Arrange(new Rect(-extras, 0, finalSize.Width + extras, finalSize.Height));
            }
        }

        return finalSize;
    }

    // Only the running pill has extras; when it is hidden its last measured width is stale.
    private double ExtrasWidth() => Pill.IsVisible ? Extras.DesiredSize.Width : 0;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = DataContext as SessionControlViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        UpdateOpen();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ApplyMotion();
        Services.Motion.SpeedChanged += OnMotionChanged;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Services.Motion.SpeedChanged -= OnMotionChanged;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnMotionChanged(object? sender, EventArgs e)
    {
        ApplyMotion();
    }

    private void ApplyMotion()
    {
        _extrasTransitions = Services.Motion.Gate(Extras, _extrasTransitions);
        EndedTarget.Classes.Set(FadeClass, Services.Motion.Current is not { IsOn: false });
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SessionControlViewModel.ForcesOpen) or nameof(SessionControlViewModel.IsRunningOrAway))
        {
            UpdateOpen();
        }
    }

    // Only keyboard focus opens it: a click leaves the focus on the button, which must not keep End in view after the pointer leaves.
    private void OnPillGotFocus(object? sender, FocusChangedEventArgs e)
    {
        _keyboardFocus = e.NavigationMethod is NavigationMethod.Tab or NavigationMethod.Directional;
        UpdateOpen();
    }

    private void OnPillLostFocus(object? sender, FocusChangedEventArgs e)
    {
        // Focus moving between the pill's own buttons raises lost then got; read where it ended up once both have run.
        Dispatcher.UIThread.Post(() =>
        {
            if (!Pill.IsKeyboardFocusWithin)
            {
                _keyboardFocus = false;
            }

            UpdateOpen();
        });
    }

    private void UpdateOpen()
    {
        bool open = _viewModel is { IsRunningOrAway: true }
            && (_viewModel.ForcesOpen || _pointerOver || _menuOpen || (_keyboardFocus && Pill.IsKeyboardFocusWithin));
        Pill.Classes.Set(OpenClass, open);
    }
}
