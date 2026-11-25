using System.ComponentModel;
using AccountabiliBuddy.App.ViewModels;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace AccountabiliBuddy.App.Views;

/// <summary>
/// The Archive sheet over the Tasks card (Tasks redesign 6.01-6.05). Code-behind: the sheet's height (66% of the card, never under
/// 320 px), the slide up and down with the dim fading in and out (0.28 s, cubic-bezier(0.2, 0.8, 0.2, 1); at once with Animations off),
/// hiding it once it is down (so it can't be clicked or tabbed into), the shadow in the theme's Ab.Shadow colour, and Esc.
/// </summary>
public partial class ArchiveSheetView : UserControl
{
    private const double MinHeightPx = 320;
    private const double HeightShare = 0.66;
    private static readonly TimeSpan SlideTime = TimeSpan.FromSeconds(0.28);
    private static readonly Easing SlideEasing = new SplineEasing(0.2, 0.8, 0.2, 1);

    private readonly TranslateTransform _slide = new();
    private TasksViewModel? _subscribed;
    private bool _isOpen;
    private int _closeTicket;

    public ArchiveSheetView()
    {
        InitializeComponent();
        Sheet.RenderTransform = _slide;
        ActualThemeVariantChanged += (_, _) => ApplyShadow();
    }

    /// <summary>The sheet went down (Esc, the dim, the fold button or the Archive row): the page puts the keyboard back on the row.</summary>
    public event EventHandler? Closed;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ApplyShadow();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_subscribed is not null)
        {
            _subscribed.PropertyChanged -= OnPagePropertyChanged;
        }

        _subscribed = DataContext as TasksViewModel;
        if (_subscribed is not null)
        {
            _subscribed.PropertyChanged += OnPagePropertyChanged;
        }

        SetOpen(_subscribed?.IsArchiveOpen == true);
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        double height = Math.Min(e.NewSize.Height, Math.Max(MinHeightPx, Math.Round(e.NewSize.Height * HeightShare)));
        Sheet.Height = height;
        if (!_isOpen)
        {
            WithoutTransitions(() => _slide.Y = height + 60);
        }
    }

    private void OnPagePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TasksViewModel.IsArchiveOpen))
        {
            SetOpen(_subscribed?.IsArchiveOpen == true);
        }
    }

    private void SetOpen(bool open)
    {
        if (open == _isOpen)
        {
            return;
        }

        _isOpen = open;
        _closeTicket++;
        bool animate = MotionOn();
        double downY = (double.IsNaN(Sheet.Height) ? Bounds.Height : Sheet.Height) + 60;
        if (open)
        {
            WithoutTransitions(() =>
            {
                _slide.Y = downY;
                Scrim.Opacity = 0;
            });
            Root.IsVisible = true;
            if (!animate)
            {
                _slide.Y = 0;
                Scrim.Opacity = 1;
                Sheet.Focus();
                return;
            }

            // After the first layout of the shown sheet, so the transition runs from the bottom.
            Dispatcher.UIThread.Post(() =>
            {
                if (!_isOpen)
                {
                    return;
                }

                SetTransitions(true);
                _slide.Y = 0;
                Scrim.Opacity = 1;
                Sheet.Focus();
            }, DispatcherPriority.Render);
            return;
        }

        bool hadFocus = Root.IsKeyboardFocusWithin;
        SetTransitions(animate);
        _slide.Y = downY;
        Scrim.Opacity = 0;
        if (!animate)
        {
            Root.IsVisible = false;
        }
        else
        {
            int ticket = _closeTicket;
            DispatcherTimer.RunOnce(() =>
            {
                if (ticket == _closeTicket && !_isOpen)
                {
                    Root.IsVisible = false;
                }
            }, SlideTime);
        }

        if (hadFocus)
        {
            Closed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void SetTransitions(bool on)
    {
        _slide.Transitions = on
            ? [new DoubleTransition { Property = TranslateTransform.YProperty, Duration = SlideTime, Easing = SlideEasing }]
            : null;
        Scrim.Transitions = on
            ? [new DoubleTransition { Property = OpacityProperty, Duration = SlideTime, Easing = SlideEasing }]
            : null;
    }

    private void WithoutTransitions(Action change)
    {
        SetTransitions(false);
        change();
    }

    // The app's Animations setting: the main window carries "motionOn" while it is on.
    private bool MotionOn()
    {
        return TopLevel.GetTopLevel(this) is Window window && window.Classes.Contains("motionOn");
    }

    // 6.04: shadow 0 −18 50 in the theme's Ab.Shadow colour (a BoxShadows value can't take a dynamic colour in XAML).
    private void ApplyShadow()
    {
        Color color = this.TryFindResource("Ab.ShadowColor", ActualThemeVariant, out object? found) && found is Color shadow ? shadow : Colors.Transparent;
        Sheet.BoxShadow = new BoxShadows(new BoxShadow { OffsetX = 0, OffsetY = -18, Blur = 50, Spread = 0, Color = color });
    }

    private void OnScrimPressed(object? sender, PointerPressedEventArgs e)
    {
        _subscribed?.Archive.CloseCommand.Execute(null);
        e.Handled = true;
    }

    // Esc: clears the Archive's search first while the keyboard is in it, then closes the sheet.
    private void OnSheetKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || e.Handled || _subscribed is null)
        {
            return;
        }

        NotebookViewModel archive = _subscribed.Archive;
        if (archive.IsSearchFiltersOpen)
        {
            // Item 7: the Search filters panel closes first.
            archive.IsSearchFiltersOpen = false;
        }
        else if (archive.IsSearching && TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox { Name: "SearchBox" })
        {
            archive.Search = string.Empty;
        }
        else
        {
            archive.CloseCommand.Execute(null);
        }

        e.Handled = true;
    }
}
