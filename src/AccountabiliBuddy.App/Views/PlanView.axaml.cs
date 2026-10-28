using Avalonia.Controls;

namespace AccountabiliBuddy.App.Views;

/// <summary>The Plan page; for now it holds the day plan and the Calendar under a page title.</summary>
public partial class PlanView : UserControl
{
    public PlanView()
    {
        InitializeComponent();
    }

    /// <summary>Scrolls the Calendar section into view (a search result for a date opens it).</summary>
    public void ShowCalendar()
    {
        CalendarSection.BringIntoView();
    }
}
