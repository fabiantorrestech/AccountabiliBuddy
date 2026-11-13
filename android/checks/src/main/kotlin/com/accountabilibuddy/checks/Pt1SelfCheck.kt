package com.accountabilibuddy.checks

import com.accountabilibuddy.companion.ui.tasks.page.TaskViewWords
import com.accountabilibuddy.shared.tasks.TaskBin
import com.accountabilibuddy.shared.tasks.TaskItem
import com.accountabilibuddy.shared.tasks.TaskLabel
import com.accountabilibuddy.shared.tasks.TaskNumber
import com.accountabilibuddy.shared.tasks.TaskSearch
import com.accountabilibuddy.shared.tasks.TaskView
import com.accountabilibuddy.shared.tasks.TaskViewConfig
import com.accountabilibuddy.shared.tasks.TaskViewContext
import com.accountabilibuddy.shared.tasks.TaskViewIcons
import com.accountabilibuddy.shared.tasks.TaskViewRules
import java.time.OffsetDateTime
import java.time.ZoneOffset

/**
 * PT1 checks with no C# twin (the phone's own UI rules): the Group by pill segment (Bins, Label, Tag, None, in the PC menu's order and
 * values; an unknown grouping lights Bins), the view icons (the PC's ten, a built-in's own name drawn, none or an unknown one the
 * bookmark, never the same pick twice), the search hint, and a view's search going through the shared rule.
 */
object Pt1SelfCheck {
    private val at: OffsetDateTime = OffsetDateTime.parse("2026-11-10T13:00:00Z")

    fun run(report: CheckReport) {
        val pills = TaskViewWords.GROUP_PILLS
        report.check("pt1: Group by pills are Bins, Label, Tag, None", pills.map { it.second } == listOf("Bins", "Label", "Tag", "None"), "$pills")
        report.check(
            "pt1: Group by pills carry the view's grouping values",
            pills.map { it.first } == listOf(TaskViewConfig.GROUP_BINS, TaskViewConfig.GROUP_LABEL, TaskViewConfig.GROUP_TAG, TaskViewConfig.GROUP_NONE),
        )
        report.check("pt1: Group by pill follows the grouping", pills.indices.all { TaskViewWords.groupPill(pills[it].first) == it })
        report.check("pt1: an unknown grouping lights Bins", TaskViewWords.groupPill("someday") == 0)
        report.check("pt1: every grouping the sheet used to list is still one pill", TaskViewWords.GROUPS.map { it.first } == pills.map { it.first })

        report.check("pt1: the picker offers ten icons, no repeats", TaskViewIcons.NAMES.size == 10 && TaskViewIcons.NAMES.toSet().size == 10)
        report.check("pt1: no icon draws the bookmark", TaskViewIcons.drawn(null) == "bookmark-outline" && TaskViewIcons.drawn(" ") == "bookmark-outline")
        report.check("pt1: an unknown icon draws the bookmark", TaskViewIcons.drawn("zz-not-an-icon") == "bookmark-outline" && TaskViewIcons.picked("zz-not-an-icon") == null)
        report.check(
            "pt1: the picker lights the bookmark for none, nothing for an unknown name, the pick for a known one",
            TaskViewIcons.picked(null) == TaskViewIcons.DEFAULT && TaskViewIcons.picked(" ") == TaskViewIcons.DEFAULT &&
                TaskViewIcons.picked("zz-not-an-icon") == null && TaskViewIcons.NAMES.all { TaskViewIcons.picked(it) == it },
        )
        report.check("pt1: a known icon draws itself", TaskViewIcons.NAMES.all { TaskViewIcons.drawn(it) == it })
        report.check("pt1: the built-in views' icons are drawn as they are", TaskView.builtIns(at).all { TaskViewIcons.drawn(it.icon) == it.icon }, TaskView.builtIns(at).map { it.icon }.toString())

        report.check("pt1: the search hint", TaskSearch.HINT == "Search · @label #tag", TaskSearch.HINT)

        // The view's search is the shared rule: "@deep" keeps the Deep work label's task, "@" keeps all.
        val deep = TaskLabel("0a0a0a0a-0000-0000-0000-000000000001", "Deep work", "#14998C", 1.0, at)
        val tasks = listOf(task(1, "Plan", deep.id), task(2, "Mail", null))
        fun shown(search: String?) = TaskViewRules.apply(tasks, TaskViewConfig.EVERYTHING, TaskViewContext(at, ZoneOffset.UTC, listOf(deep), search))
            .groups.sumOf { it.tasks.size }
        report.check("pt1: a view's search takes @label from the shared rule", shown("@deep") == 1 && shown("@deep_work") == 1 && shown("@x") == 0)
        // A saved view's search text replays through the shared rule: a number-like word that is no task number is a word.
        val numbered = listOf(task(1, "10am standup", null), task(2, "1st draft", null), task(3, "Mail", null))
        fun replay(search: String) = TaskViewRules.apply(numbered, TaskViewConfig.EVERYTHING, TaskViewContext(at, ZoneOffset.UTC, listOf(deep), search))
            .groups.flatMap { it.tasks }.map { it.title }
        report.check("pt1: a saved view's 10am finds the word", replay("10am") == listOf("10am standup") && replay("1st") == listOf("1st draft"), "${replay("10am")}")
        report.check("pt1: a saved view's 101 still goes by number", replay("101") == listOf("10am standup") && replay("999").isEmpty())
        report.check(
            "pt1: folding is per code point, same as the PC",
            TaskSearch.fold("\u039F\u0394\u039F\u03A3 \u0130 AB") == "\u03BF\u03B4\u03BF\u03C3 \u0130 ab" && !TaskSearch.isId("\u0661\u0662\u0663"),
        )
        report.check("pt1: a lone @ or blank search shows everything", shown("@") == 2 && shown("  ") == 2 && shown(null) == 2)
    }

    private fun task(n: Int, title: String, label: String?): TaskItem =
        TaskItem("90909090-0000-0000-0000-%012d".format(n), title, TaskBin.NEXT, n.toDouble(), at, at, at, number = TaskNumber(100 + n), labelId = label)
}
