using AccountabiliBuddy.App.ViewModels;
using AccountabiliBuddy.Core.Domain;
using Avalonia.Controls;

namespace AccountabiliBuddy.App.Views;

/// <summary>Opens the event menu (ADR 64) at a control, and its "Always classify like this" dialog over the window that control is in.</summary>
public static class CalendarEventMenuPresenter
{
    public static void Show(Control anchor, CalendarEventMenu menu, CalendarEvent calendarEvent, PlacementMode placement)
    {
        TaskMenus.ShowEntries(anchor, menu.EntriesFor(calendarEvent, () => AskKeywordRule(anchor, menu, calendarEvent)), placement);
    }

    private static async void AskKeywordRule(Control anchor, CalendarEventMenu menu, CalendarEvent calendarEvent)
    {
        if (TopLevel.GetTopLevel(anchor) is not Window owner)
        {
            return;
        }

        var dialog = new EventRuleDialog(menu.SuggestKeyword(calendarEvent), menu.Categories);
        if (await dialog.ShowDialog<bool>(owner) && dialog.ChosenCategoryId is Guid category)
        {
            menu.SaveKeywordRule(calendarEvent, dialog.Keyword, category);
        }
    }
}
