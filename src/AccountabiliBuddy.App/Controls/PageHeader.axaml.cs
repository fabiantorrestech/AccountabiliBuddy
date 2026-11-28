using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AccountabiliBuddy.App.Controls;

/// <summary>
/// The shared page header (round 2, brief §3.3; Tasks redesign 4a): <see cref="Title"/> and <see cref="Date"/>, then at the right the
/// page's <see cref="Search"/> (if any), its own controls (<see cref="Actions"/>) and the bordered Options (tune) button, on one line
/// (<see cref="PageHeaderRow"/>: the date shortens first, then the search narrows). A click on Options raises <see cref="OptionsRequested"/>; the page
/// opens its Options menu under <see cref="OptionsAnchor"/> and calls <see cref="SetOptionsOpen"/> while it is open.
/// </summary>
public partial class PageHeader : UserControl
{
    public static readonly StyledProperty<string> TitleProperty = AvaloniaProperty.Register<PageHeader, string>(nameof(Title), string.Empty);

    public static readonly StyledProperty<string> DateProperty = AvaloniaProperty.Register<PageHeader, string>(nameof(Date), string.Empty);

    public static readonly StyledProperty<object?> ActionsProperty = AvaloniaProperty.Register<PageHeader, object?>(nameof(Actions));

    public static readonly StyledProperty<object?> SearchProperty = AvaloniaProperty.Register<PageHeader, object?>(nameof(Search));

    public static readonly StyledProperty<bool> FadesOptionsProperty = AvaloniaProperty.Register<PageHeader, bool>(nameof(FadesOptions));

    /// <summary>A click on the Options button.</summary>
    public static readonly RoutedEvent<RoutedEventArgs> OptionsRequestedEvent =
        RoutedEvent.Register<PageHeader, RoutedEventArgs>(nameof(OptionsRequested), RoutingStrategies.Bubble);

    private const string OpenClass = "open";

    // idleFade lights a control while it carries this class (Themes/AbStyles.axaml).
    private const string OnClass = "on";

    private const string IdleFadeClass = "idleFade";

    public PageHeader()
    {
        InitializeComponent();
        Row.TitlePart = TitleText;
        Row.DatePart = DateText;
        Row.SearchPart = SearchHost;
        Row.ActionsPart = ActionsHost;
        Row.OptionsPart = OptionsButton;
        SearchHost.IsVisible = false;
        ShowTitle();
    }

    /// <summary>The page's name ("Tasks").</summary>
    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>Today's date after the title ("Tuesday · Nov 24", <see cref="ViewModels.PageDateText"/>).</summary>
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

    /// <summary>The page's search box, before its own controls: 300 wide, narrowing to 140 once the date is as short as it gets.</summary>
    public object? Search
    {
        get => GetValue(SearchProperty);
        set => SetValue(SearchProperty, value);
    }

    /// <summary>The Options button rests faint until hovered, focused or open ("Fade controls you're not using"; Tasks).</summary>
    public bool FadesOptions
    {
        get => GetValue(FadesOptionsProperty);
        set => SetValue(FadesOptionsProperty, value);
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
        OptionsButton.Classes.Set(OnClass, open);
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
        else if (change.Property == SearchProperty)
        {
            SearchHost.Content = Search;
            SearchHost.IsVisible = Search is not null;
        }
        else if (change.Property == FadesOptionsProperty)
        {
            OptionsButton.Classes.Set(IdleFadeClass, FadesOptions);
        }
    }

    private void ShowTitle()
    {
        TitleText.Text = Title;
        DateText.Text = Date;
        DateText.IsVisible = Date.Length > 0;
        // "Saturday, Nov 28th" shortens to "Saturday, …" before the search narrows.
        int comma = Date.IndexOf(ViewModels.PageDateText.Separator, StringComparison.Ordinal);
        Row.DateMinText = comma > 0 ? Date[..comma] + ViewModels.PageDateText.Separator + "…" : Date;
        Row.InvalidateMeasure();
        string name = Title.Length > 0 ? Title + " options" : "Options";
        AutomationProperties.SetName(OptionsButton, name);
    }

    private void OnOptionsClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        RaiseEvent(new RoutedEventArgs(OptionsRequestedEvent, this));
    }
}
