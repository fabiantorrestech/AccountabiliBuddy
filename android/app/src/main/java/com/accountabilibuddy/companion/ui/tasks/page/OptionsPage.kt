package com.accountabilibuddy.companion.ui.tasks.page

/** The pages of the Tasks Options sheet ([TasksOptionsSheet]): the sheet itself and the sub-sheets its rows open, each with a back arrow. */
enum class OptionsPage(val title: String, val subtitle: String?) {
    Main("Options", "View, filter and sort"),
    Views("Saved views", "A view keeps the grouping, sort and filters"),
    CreateView("Create a view", "Saves the grouping, sort and filters as they are now"),
    ViewIcon("View icon", "Shown beside the view in the list"),
    PageOrder("Page order", "Drag ≡ to set left to right. Tasks opens on your start page."),
    Sort("Sort", null),
    Due("Due date", null),
    Labels("Labels", null),
    Tags("Tags", null),
    Time("Time limit", null),
    Old("Untouched for", null),
    History("History", "Finished and deleted tasks, 30 days"),
}
