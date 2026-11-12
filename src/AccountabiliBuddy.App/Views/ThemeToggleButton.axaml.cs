using AccountabiliBuddy.App.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;

namespace AccountabiliBuddy.App.Views;

/// <summary>
/// The title bar's light/dark toggle. A click flips light and dark; a right-click, holding the button about 450 ms, Shift+F10
/// or the Menu key opens the theme menu (a hold that opened the menu doesn't also flip). The icon follows the theme actually
/// shown (Windows' own while matching Windows).
/// </summary>
public partial class ThemeToggleButton : UserControl
{
    private const string OpenClass = "open";
    private static readonly TimeSpan HoldTime = TimeSpan.FromMilliseconds(450);

    private readonly DispatcherTimer _holdTimer = new() { Interval = HoldTime };

    // The current press opened the menu by holding, so its release is not a click.
    private bool _openedByHold;

    public ThemeToggleButton()
    {
        InitializeComponent();
        _holdTimer.Tick += (_, _) =>
        {
            _holdTimer.Stop();
            _openedByHold = true;
            OpenMenu();
        };
        ToggleButton.AddHandler(PointerPressedEvent, OnPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        ToggleButton.AddHandler(PointerReleasedEvent, (_, _) => _holdTimer.Stop(), RoutingStrategies.Tunnel, handledEventsToo: true);
        ToggleButton.PointerCaptureLost += (_, _) => _holdTimer.Stop();
        ToggleButton.Click += OnClick;
        ToggleButton.ContextRequested += (_, e) =>
        {
            OpenMenu();
            e.Handled = true;
        };
        if (Menu is { } menu)
        {
            menu.Opened += (_, _) => ToggleButton.Classes.Set(OpenClass, true);
            menu.Closed += (_, _) => ToggleButton.Classes.Set(OpenClass, false);
        }

        ActualThemeVariantChanged += (_, _) => ShowVariant();
    }

    /// <summary>The theme menu, for the title bar's one-pop-up-at-a-time rule.</summary>
    public FlyoutBase? Menu => FlyoutBase.GetAttachedFlyout(ToggleButton);

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        ShowVariant();
    }

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        _openedByHold = false;
        if (e.GetCurrentPoint(ToggleButton).Properties.IsLeftButtonPressed)
        {
            _holdTimer.Stop();
            _holdTimer.Start();
        }
    }

    private void OnClick(object? sender, RoutedEventArgs e)
    {
        _holdTimer.Stop();
        if (_openedByHold)
        {
            _openedByHold = false;
            return;
        }

        if (DataContext is ThemeToggleViewModel viewModel && viewModel.ToggleCommand.CanExecute(null))
        {
            viewModel.ToggleCommand.Execute(null);
        }
    }

    private void OpenMenu()
    {
        if (Menu is { IsOpen: false } menu)
        {
            menu.ShowAt(ToggleButton);
        }
    }

    private void ShowVariant()
    {
        if (DataContext is ThemeToggleViewModel viewModel)
        {
            viewModel.IsDarkShown = ActualThemeVariant == ThemeVariant.Dark;
        }
    }
}
