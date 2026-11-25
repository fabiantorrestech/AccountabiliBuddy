using AccountabiliBuddy.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;

namespace AccountabiliBuddy.App.Services.Search;

/// <summary>
/// Opens a search result: the page first, then what to show in it (a profile, a date, a Settings row), and last the
/// control the text was found in, which the window brings into view and highlights.
/// </summary>
public sealed class SearchNavigator
{
    private readonly Action<int> _selectTab;
    private readonly Action<Guid> _selectProfile;
    private readonly Action<DateOnly> _showDate;
    private readonly Action<SettingsSection> _showSettingsGroup;
    private readonly Action<int> _showPlanTemplate;
    private readonly Action<object?> _reveal;
    private readonly Action<IReadOnlyList<Guid>>? _showLabels;

    /// <param name="reveal">Brings the control (or, when null, the page's first match) into view once laid out.</param>
    /// <param name="showLabels">A label or category result: the Tasks page's label filter set to just those labels.</param>
    public SearchNavigator(
        Action<int> selectTab,
        Action<Guid> selectProfile,
        Action<DateOnly> showDate,
        Action<SettingsSection> showSettingsGroup,
        Action<int> showPlanTemplate,
        Action<object?> reveal,
        Action<IReadOnlyList<Guid>>? showLabels = null)
    {
        _selectTab = selectTab;
        _selectProfile = selectProfile;
        _showDate = showDate;
        _showSettingsGroup = showSettingsGroup;
        _showPlanTemplate = showPlanTemplate;
        _reveal = reveal;
        _showLabels = showLabels;
    }

    public void Open(SearchTarget target)
    {
        _selectTab(target.Tab);
        if (target.ProfileId is Guid profileId)
        {
            _selectProfile(profileId);
        }

        if (target.Date is DateOnly date)
        {
            _showDate(date);
        }

        if (target.SettingsGroup is SettingsSection group)
        {
            _showSettingsGroup(group);
        }

        if (target.Labels is { Count: > 0 } labels)
        {
            _showLabels?.Invoke(labels);
        }

        if (target.PlanTemplate is int template)
        {
            _showPlanTemplate(template);
        }

        if (target.Control is not null)
        {
            _reveal(target.Control);
        }

        // Words inside a flyout: it opens on the button that holds it, once the page is showing.
        if (target.Flyout is FlyoutBase flyout && target.Control is Control { IsEffectivelyVisible: true } owner)
        {
            Dispatcher.UIThread.Post(() => flyout.ShowAt(owner), DispatcherPriority.Background);
        }
    }
}
