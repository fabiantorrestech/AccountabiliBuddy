namespace AccountabiliBuddy.App.Services.Search;

/// <summary>
/// Opens a search result: the tab first, then what to show in it (a profile, a date, a Settings group), and last the
/// control the text was found in, which the window brings into view and highlights.
/// </summary>
public sealed class SearchNavigator
{
    private readonly Action<int> _selectTab;
    private readonly Action<Guid> _selectProfile;
    private readonly Action<DateOnly> _showDate;
    private readonly Action<int> _showSettingsGroup;
    private readonly Action<int> _showPlanTemplate;
    private readonly Action<object?> _reveal;

    /// <param name="reveal">Brings the control (or, when null, the page's first match) into view once laid out.</param>
    public SearchNavigator(
        Action<int> selectTab,
        Action<Guid> selectProfile,
        Action<DateOnly> showDate,
        Action<int> showSettingsGroup,
        Action<int> showPlanTemplate,
        Action<object?> reveal)
    {
        _selectTab = selectTab;
        _selectProfile = selectProfile;
        _showDate = showDate;
        _showSettingsGroup = showSettingsGroup;
        _showPlanTemplate = showPlanTemplate;
        _reveal = reveal;
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

        if (target.SettingsGroup is int group)
        {
            _showSettingsGroup(group);
        }

        if (target.PlanTemplate is int template)
        {
            _showPlanTemplate(template);
        }

        if (target.Control is not null)
        {
            _reveal(target.Control);
        }
    }
}
