using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AccountabiliBuddy.App.Controls;

/// <summary>
/// The shared page header (round 2, brief §3.3): <see cref="Title"/> and <see cref="Date"/>, the page's own controls
/// (<see cref="Actions"/>), then the bordered Options (tune) button. A click on Options raises <see cref="OptionsRequested"/>; the page
/// opens its Options menu under <see cref="OptionsAnchor"/> and calls <see cref="SetOptionsOpen"/> while it is open.
/// </summary>
public partial class PageHeader : UserControl
{
    public static readonly StyledProperty<string> TitleProperty = AvaloniaProperty.Register<PageHeader, string>(nameof(Title), string.Empty);

    public static readonly StyledProperty<string> DateProperty = AvaloniaProperty.Register<PageHeader, string>(nameof(Date), string.Empty);

    public static readonly StyledProperty<object?> ActionsProperty = AvaloniaProperty.Register<PageHeader, object?>(nameof(Actions));

    /// <summary>A click on the Options button.</summary>
    public static readonly RoutedEvent<RoutedEventArgs> OptionsRequestedEvent =
        RoutedEvent.Register<PageHeader, RoutedEventArgs>(nameof(OptionsRequested), RoutingStrategies.Bubble);

    private const string OpenClass = "open";

    public PageHeader()
    {
        InitializeComponent();
        ShowTitle();
    }

    /// <summary>The page's name ("Tasks").</summary>
    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>Today's date, muted, after the title ("Wed 11/11"; brief §3.11).</summary>
    public string Date
    {
        get => GetValue(DateProperty);
        set => SetValue(DateProperty, value);
    }

    /// <summary>The page's own header controls, between the date and Options.</summary>
    public object? Actions
    {
        get => GetValue(ActionsProperty);
        set => SetValue(ActionsProperty, value);
    }

    public event EventHandler<RoutedEventArgs>? OptionsRequested
    {
        add => AddHandler(OptionsRequestedEvent, value);
        remove => RemoveHandler(OptionsRequestedEvent, value);
    }

    /// <summary>The Options button: the page's Options menu opens under it, aligned to its right edge.</summary>
    public Control OptionsAnchor => OptionsButton;

    /// <summary>The Options button looks pressed while its menu is open.</summary>
    public void SetOptionsOpen(bool open)
    {
        OptionsButton.Classes.Set(OpenClass, open);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TitleProperty || change.Property == DateProperty)
        {
            ShowTitle();
        }
        else if (change.Property == ActionsProperty)
        {
            ActionsHost.Content = Actions;
        }
    }

    private void ShowTitle()
    {
        TitleText.Text = Title;
        DateText.Text = Date;
        DateText.IsVisible = Date.Length > 0;
        string name = Title.Length > 0 ? Title + " options" : "Options";
        AutomationProperties.SetName(OptionsButton, name);
    }

    private void OnOptionsClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        RaiseEvent(new RoutedEventArgs(OptionsRequestedEvent, this));
    }
}
