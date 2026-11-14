package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.RunningSession
import com.accountabilibuddy.shared.WatchSnapshot
import com.accountabilibuddy.shared.WatchSnapshotJson
import com.accountabilibuddy.shared.sync.FieldStamp
import com.accountabilibuddy.shared.sync.SessionTaskItem
import com.accountabilibuddy.shared.sync.SyncCategory
import com.accountabilibuddy.shared.sync.SyncNextTask
import com.accountabilibuddy.shared.sync.TaskLabels
import com.accountabilibuddy.shared.tasks.OldPaneScopes
import com.accountabilibuddy.shared.tasks.ShelfEdits
import com.accountabilibuddy.shared.tasks.ShelfPage
import com.accountabilibuddy.shared.tasks.ShelfPages
import com.accountabilibuddy.shared.tasks.SyncTagScope
import com.accountabilibuddy.shared.tasks.SyncTaskItem
import com.accountabilibuddy.shared.tasks.SyncTaskLabel
import com.accountabilibuddy.shared.tasks.TaskBin
import com.accountabilibuddy.shared.tasks.TaskField
import com.accountabilibuddy.shared.tasks.TaskFieldStamps
import com.accountabilibuddy.shared.tasks.TaskItem
import com.accountabilibuddy.shared.tasks.TaskLabel
import com.accountabilibuddy.shared.tasks.TaskLabelEdits
import com.accountabilibuddy.shared.tasks.TaskLists
import com.accountabilibuddy.shared.tasks.TaskNumber
import com.accountabilibuddy.shared.tasks.TaskScopeRules
import com.accountabilibuddy.shared.tasks.TaskScopeView
import com.accountabilibuddy.shared.tasks.TaskScopes
import com.accountabilibuddy.shared.tasks.TaskViewConfig
import java.io.File
import java.time.Instant
import java.time.LocalDate
import java.time.OffsetDateTime
import java.time.ZoneId

/**
 * LB1c (phone UI for device-only labels and tags, ADR 89): what THIS phone shows ([TaskScopeView]), the picker groups, "No label" for a
 * hidden label, search that finds nothing hidden, the old pane's rows, the watch snapshot, every phone edit path keeping hidden labels and
 * tags, "Make shared" only after a confirm, a removed device's labels shown as shared, and scans of the Compose wiring (no Context here).
 * Same fixtures as LB1a (a work PC's "Work", this phone's "Gym", a shared "Home", one both own, one of a removed device).
 */
object Lb1cSelfCheck {
    private const val PC = "aaaaaaaa-0000-0000-0000-00000000000a"
    private const val PHONE = "bbbbbbbb-0000-0000-0000-00000000000b"
    private const val GONE = "dddddddd-0000-0000-0000-00000000000d"
    private val t0: OffsetDateTime = OffsetDateTime.parse("2026-11-12T12:00:00Z")
    private val zone: ZoneId = ZoneId.of("UTC")
    private val removed = setOf(GONE)

    private fun id(n: Int) = "73737373-0000-0000-0000-%012d".format(n)

    private fun label(name: String, pos: Double, onlyOn: List<String>?) =
        TaskLabel(TaskLabel.idFor(name), name, "#2F6BEA", pos, t0, onlyOn = onlyOn, onlyOnStamp = onlyOn?.let { FieldStamp(t0, it.first()) })

    private fun task(n: Int, labelId: String?, tags: List<String>?, bin: TaskBin = TaskBin.NEXT) =
        SyncTaskItem(TaskItem(id(n), "Task $n", bin, n.toDouble(), t0, t0, t0, number = TaskNumber(100 + n), labelId = labelId, tags = tags), TaskFieldStamps.allAt(FieldStamp(t0, PC)))

    private val work = label("Work", 1.0, listOf(PC))
    private val home = label("Home", 2.0, null)
    private val gym = label("Gym", 3.0, listOf(PHONE))
    private val both = label("Both", 4.0, listOf(PC, PHONE))
    private val old = label("Old", 5.0, listOf(GONE))

    private fun world() = TaskLists(
        listOf(task(1, work.id, listOf("secret", "plain")), task(2, home.id, null), task(3, gym.id, listOf("gym")), task(4, old.id, null), task(5, work.id, null)),
        listOf(work, home, gym, both, old).map { SyncTaskLabel(it) }, emptyList(), emptyList(), null,
        listOf(SyncTagScope(TaskScopes.tagIdFor("secret"), "secret", listOf(PC), t0), SyncTagScope(TaskScopes.tagIdFor("gym"), "gym", listOf(PHONE), t0)),
    )

    private fun view(lists: TaskLists = world(), self: String = PHONE) = TaskScopeView(lists, self, removed)

    private fun count(pages: List<ShelfPage>) = pages.flatMap { it.tasks }.map { it.id }.distinct().size

    private fun name(id: String) = if (id == PC) "Work PC" else if (id == PHONE) "this phone" else "Old tablet"

    fun run(report: CheckReport) {
        val v = view()
        // Pickers: grouped, hidden never shown, a removed device's label shared.
        report.check("lb1c: this phone shows Home, Gym, Both and Old, never the work PC's Work", v.labels.map { it.name } == listOf("Home", "Gym", "Both", "Old"), v.labels.map { it.name }.toString())
        val groups = v.labelGroups()
        report.check("lb1c: the label picker has two groups in order, Shared then This device", groups.map { it.title } == listOf("Shared", "This device") && groups.map { it.deviceOnly } == listOf(false, true))
        report.check("lb1c: Shared holds Home and the removed device's Old (a removed device's labels show as shared)", groups[0].items.map { it.name } == listOf("Home", "Old"), groups[0].items.map { it.name }.toString())
        report.check("lb1c: This device holds Gym and Both (a label this phone shares with another device is under This device)", groups[1].items.map { it.name } == listOf("Gym", "Both"))
        report.check("lb1c: a label several devices share says 'on this phone, <device>' (this phone first)", v.devicesLine(both.onlyOn, ::name) == "on this phone, Work PC" && v.devicesLine(home.onlyOn, ::name) == null)
        report.check("lb1c: a label of a removed device reads as shared (no 'on' line)", v.devicesLine(old.onlyOn, ::name) == null)
        val tagGroups = v.tagGroups(v.knownTags())
        report.check("lb1c: the tag picker groups Shared (plain) then This device (gym); the work PC's 'secret' isn't offered", tagGroups.map { it.title to it.items } == listOf("Shared" to listOf("plain"), "This device" to listOf("gym")), tagGroups.toString())
        report.check("lb1c: a phone with no id shows everything", view(self = FieldStamp.NOBODY).let { it.labels.size == 5 && it.shown() === it.lists && it.categoryShown("Work") })

        // Hidden reads No label; the real list keeps it.
        val shown = v.shown()
        val t1 = shown.tasks.first { it.id == id(1) }.task
        report.check("lb1c: a task whose only label is hidden reads No label, and its hidden tag is off its tags", t1.labelId == null && t1.tags == listOf("plain") && TaskScopeRules.labelText(v, t1.labelId) == "No label")
        report.check("lb1c: the real list is untouched by showing (label and both tags kept)", world().tasks.first { it.id == id(1) }.task.let { it.labelId == work.id && it.tags == listOf("secret", "plain") })
        report.check("lb1c: the shown list has no hidden label at all", shown.labels.none { it.label.name == "Work" })
        val pagesByLabel = ShelfPages.build(shown, TaskViewConfig.EVERYTHING.copy(group = TaskViewConfig.GROUP_LABEL), t0, zone)
        report.check(
            "lb1c: Group by Label has no Work page; its tasks are under No label",
            pagesByLabel.none { it.title == "Work" } && pagesByLabel.firstOrNull { it.isNone }?.tasks?.map { it.id }?.containsAll(listOf(id(1), id(5))) == true, pagesByLabel.map { it.title }.toString(),
        )
        val pagesByTag = ShelfPages.build(shown, TaskViewConfig.EVERYTHING.copy(group = TaskViewConfig.GROUP_TAG), t0, zone)
        report.check("lb1c: Group by Tag has no #secret page", pagesByTag.none { it.title.contains("secret") } && pagesByTag.any { it.title.contains("plain") }, pagesByTag.map { it.title }.toString())

        // Search.
        fun found(text: String) = count(ShelfPages.build(shown, TaskViewConfig.EVERYTHING, t0, zone, text))
        report.check(
            "lb1c: @Work and #secret find nothing; @Home and #plain find their tasks",
            found("@Work") == 0 && found("#secret") == 0 && found("@Home") == 1 && found("#plain") == 1, "${found("@Work")} ${found("#secret")} ${found("@Home")} ${found("#plain")}",
        )
        report.check(
            "lb1c: a saved view that filters by a hidden label says 'Uses a label from <device>' (the device, never the label's name)",
            v.viewNote(listOf(work.id), null, ::name) == "Uses a label from Work PC" && v.viewNote(listOf(home.id), null, ::name) == null && v.viewNote(null, listOf("secret"), ::name) == "Uses a tag from Work PC",
        )
        report.check("lb1c: a view's picked count leaves out hidden ones", v.shownLabelCount(listOf(work.id, home.id)) == 1 && v.shownTagCount(listOf("secret", "plain")) == 1)

        // Every phone edit path keeps what is hidden.
        val real = world()
        fun task1(l: TaskLists) = l.tasks.first { it.id == id(1) }.task
        val later = t0.plusMinutes(1)
        val removeTag = ShelfEdits.edit(real, id(1), TaskField.TAGS, PHONE, later, removed) { it.copy(tags = null) }.lists
        report.check("lb1c: the tags page removing every shown tag keeps the hidden tag and label (edit path)", task1(removeTag).tags == listOf("secret") && task1(removeTag).labelId == work.id)
        val noLabel = ShelfEdits.editAll(real, listOf(id(1)), TaskField.LABEL, PHONE, later, removed) { it.copy(labelId = null) }.lists
        report.check("lb1c: 'No label' from the picker or bulk keeps a hidden label (editAll path)", task1(noLabel).labelId == work.id && task1(noLabel).tags == listOf("secret", "plain"))
        val dropped = ShelfEdits.dropOnPage(real, listOf(id(1)), pagesByLabel.first { it.isNone }, PHONE, later, removed = removed).lists
        report.check("lb1c: dropping on the No label page keeps a hidden label (drop path)", task1(dropped).labelId == work.id)
        val renamed = ShelfEdits.rename(real, id(1), "New title", PHONE, later, removed).lists
        report.check("lb1c: renaming keeps hidden labels and tags (rename path)", task1(renamed).title == "New title" && task1(renamed).labelId == work.id && task1(renamed).tags == listOf("secret", "plain"))
        val picked = ShelfEdits.edit(real, id(1), TaskField.LABEL, PHONE, later, removed) { it.copy(labelId = home.id) }.lists
        report.check("lb1c: picking a shown label replaces a hidden one (the user's act) and keeps the hidden tag", task1(picked).labelId == home.id && task1(picked).tags == listOf("secret", "plain"))
        val added = ShelfEdits.add(real, id(90), "Typed", TaskBin.NEXT, null, listOf("secret"), PHONE, later, removed = removed).lists
        report.check(
            "lb1c: a typed tag that is hidden here is joined (this phone added to its list), never made shared (add path)",
            TaskScopes.scopeOf("secret", added.tagScopes)?.onlyOn == listOf(PC, PHONE).sorted(),
        )
        val removedLabel = ShelfEdits.edit(real, id(4), TaskField.LABEL, PHONE, later, removed) { it.copy(labelId = null) }.lists
        val removedKept = ShelfEdits.edit(real, id(4), TaskField.LABEL, PHONE, later, null) { it.copy(labelId = null) }.lists
        report.check(
            "lb1c: the removed devices are told to the writers: a removed device's label can be taken off (it is shown), unlike with no list told",
            removedLabel.tasks.first { it.id == id(4) }.task.labelId == null && removedKept.tasks.first { it.id == id(4) }.task.labelId == old.id,
        )

        // Creating.
        val joinedName = TaskLabelEdits.add(real, "Work", null, t0.plusMinutes(2), PHONE, removed = removed)
        report.check("lb1c: a new label named like one that exists on another device is joined, never made shared", TaskLabelEdits.named(joinedName, "Work")?.onlyOn == listOf(PC, PHONE).sorted())
        report.check(
            "lb1c: the form says which device has the name ('Work' is also on Work PC)",
            v.hiddenNameDevices("work", ::name) == listOf("Work PC") && v.hiddenNameDevices("Home", ::name) == null && TaskScopeRules.joinNote("Work", listOf("Work PC")).startsWith("'Work' is also on Work PC."),
        )
        val here = TaskLabelEdits.add(real, "Errands", null, t0.plusMinutes(2), PHONE, onlyOnThisDevice = true, removed = removed)
        report.check(
            "lb1c: 'Only on this phone' makes a new label this phone's own; the default is shared",
            TaskLabelEdits.named(here, "Errands")?.onlyOn == listOf(PHONE) && TaskLabelEdits.named(TaskLabelEdits.add(real, "Plain", null, t0, PHONE, removed = removed), "Plain")?.onlyOn == null,
        )

        // Make shared needs a confirm; no 'remove device' exists.
        val unconfirmed = TaskScopeRules.makeLabelShared(real, both.id, confirmed = false, now = t0.plusMinutes(3), self = PHONE)
        val confirmed = TaskScopeRules.makeLabelShared(real, both.id, confirmed = true, now = t0.plusMinutes(3), self = PHONE)
        report.check("lb1c: Make shared without a confirm changes nothing; confirmed it shares the label", unconfirmed == real && confirmed.labels.first { it.label.id == both.id }.label.onlyOn == null)
        report.check(
            "lb1c: Make shared for a tag asks the same",
            TaskScopeRules.makeTagShared(real, "gym", false, t0.plusMinutes(3), PHONE) == real && TaskScopes.scopeOf("gym", TaskScopeRules.makeTagShared(real, "gym", true, t0.plusMinutes(3), PHONE).tagScopes) == null,
        )
        val toHere = TaskScopeRules.makeLabelDeviceOnly(real, both.id, t0.plusMinutes(3), PHONE)
        report.check(
            "lb1c: 'Only on this phone' on a label several devices share joins (keeps the other device), never removes one",
            toHere.labels.first { it.label.id == both.id }.label.onlyOn == listOf(PC, PHONE).sorted() &&
                TaskScopeRules.makeLabelDeviceOnly(real, home.id, t0.plusMinutes(3), PHONE).labels.first { it.label.id == home.id }.label.onlyOn == listOf(PHONE),
        )

        // The old pane.
        val next = SyncNextTask(id(1), "Fix it", null, "Work", "#2F6BEA", null, t0.toInstant(), null, 1.0, labels = listOf("secret", "plain"))
        oldPane(report, v, next)

        // The watch.
        val session = SessionTaskItem(id(1), "Fix it", null, emptyList(), categoryId = work.id, category = "Work", categoryColor = "#2F6BEA")
        val running = RunningSession("Deep", Instant.parse("2026-11-12T11:00:00Z"), null, false).withTasks(listOf(session), null)
        val snapshot = WatchSnapshot(Instant.parse("2026-11-12T12:00:00Z"), true, "PC", LocalDate.of(2026, 11, 12), emptyList(), emptyList(), emptyList(), 0, emptyList(), running)
        val json = WatchSnapshotJson.write(snapshot).toString()
        report.check("lb1c: the watch snapshot carries no label or tag name (a session task shows its title only)", !json.contains("Work") && !json.contains("secret") && json.contains("Fix it"), json.take(200))

        sourceChecks(report)
    }

    private fun oldPane(report: CheckReport, v: TaskScopeView, next: SyncNextTask) {
        val shownNext = OldPaneScopes.nextTasks(listOf(next), v).single()
        report.check("lb1c: the old pane drops a hidden category and tag from a task (the old fields still carry them out)", shownNext.category == null && shownNext.categoryColor == null && shownNext.labels == listOf("plain"))
        val shared = next.copy(category = "Home", labels = listOf("plain"))
        report.check("lb1c: the old pane leaves a shown category and tag as they are", OldPaneScopes.nextTasks(listOf(shared), v).single() == shared)
        report.check("lb1c: the old pane's category list leaves out the hidden label", OldPaneScopes.categories(listOf(SyncCategory("Work", "#2F6BEA"), SyncCategory("Home", "#2F6BEA")), v).map { it.name } == listOf("Home"))
        val check = SessionTaskItem(id(1), "Fix it", null, emptyList(), categoryId = "x", category = "Work", categoryColor = "#2F6BEA")
        report.check("lb1c: a PC's running checklist drawn here has no hidden category", OldPaneScopes.checklist(listOf(check), v).single().category == null)
        report.check("lb1c: TaskLabels words stay (sanity: the tag helper the pane uses)", TaskLabels.clean(listOf("plain")) == listOf("plain"))
    }

    private val app = "../app/src/main/java/com/accountabilibuddy/companion"

    private fun source(path: String): String = File(path).takeIf { it.exists() }?.readText()?.replace("\r\n", "\n") ?: ""

    private fun sourceChecks(report: CheckReport) {
        val host = source("$app/ui/tasks/page/TasksPageHost.kt")
        val menu = source("$app/ui/tasks/page/TaskMenuSheet.kt")
        val bulk = source("$app/ui/tasks/page/BulkSheet.kt")
        val options = source("$app/ui/tasks/page/TasksOptionsSheet.kt")
        val picker = source("$app/ui/tasks/page/ScopePickers.kt")
        val add = source("$app/ui/tasks/page/AddTaskSheet.kt")
        val today = source("$app/ui/today/TodayTaskSections.kt")
        val labels = source("$app/ui/settings/TaskLabelsPage.kt")
        val screen = source("$app/ui/TasksScreen.kt")
        val archive = source("$app/ui/tasks/archive/ArchiveData.kt")
        val cats = source("$app/sync/TaskCategoriesStore.kt")
        val shelf = source("$app/ui/tasks/page/ShelfController.kt")
        val actions = source("$app/tasks/TaskSettingsActions.kt")
        report.check("lb1c: the wiring files are found", listOf(host, menu, bulk, options, picker, add, today, labels, screen, archive, cats, shelf, actions).all { it.isNotEmpty() })
        report.check("lb1c: the Tasks page reads the list as this phone shows it (scopeView.shown()), and its labels from the view", host.contains("val lists = remember(scopeView) { scopeView.shown() }") && host.contains("val labels = scopeView.labels"))
        report.check("lb1c: Today reads it the same way", today.contains("val lists = remember(scopeView) { scopeView.shown() }") && today.contains("val labels = scopeView.labels"))
        report.check(
            "lb1c: the ⋮ sheet, the bulk sheet and the Options filters group their labels and tags under Shared / This device",
            menu.contains("LabelGroups(scopeView)") && menu.contains("scopeView.tagGroups(") && bulk.contains("LabelGroups(scopeView)") && bulk.contains("scopeView.tagGroups(") &&
                options.contains("LabelGroups(scopeView)") && options.contains("scopeView.tagGroups("),
        )
        report.check("lb1c: no picker lists the raw label list any more", listOf(menu, bulk, options, host, today).none { Regex("""lists\.labels\.map \{ it\.label \}\.filter \{ it\.isLive \}""").containsMatchIn(it) })
        report.check(
            "lb1c: every new-label form is the scoped one (shared by default, an 'Only on this phone' switch)",
            host.contains("NewLabelForm(scopeView, canKeepHere") && menu.contains("NewLabelForm(scopeView, canKeepHere") && bulk.contains("NewLabelForm(scopeView, canKeepHere") && picker.contains("TaskScopeRules.ONLY_ON_THIS_PHONE"),
        )
        report.check(
            "lb1c: the Add sheet offers device-only labels after the shared ones and an 'Only on this phone' choice for new tags",
            add.contains(".sortedBy { it.id in deviceLabelIds }") && add.contains("onlyHereTags") && host.contains("controller.setTagScope(it, onlyHere = true)"),
        )
        report.check(
            "lb1c: Make shared on a typed name asks first (the dialog's confirm is the only call that makes it shared)",
            picker.contains("onConfirm = { asking = false; onAdd(name, false, true) }") && Regex("""onAdd\(name, \w+, true\)""").findAll(picker).count() == 1 && picker.contains("TaskScopeRules.MAKE_SHARED_ASK"),
        )
        report.check(
            "lb1c: Settings > Labels' Make shared (labels and tags) asks first and is confirmed only inside the dialog",
            labels.contains("onConfirm = { makingShared = false; actions.makeLabelShared(label.id, confirmed = true) }") && labels.contains("actions.makeTagShared(tag, confirmed = true) }") &&
                Regex("""makeLabelShared\([^)]*confirmed = true""").findAll(labels).count() == 1 && Regex("""makeTagShared\([^)]*confirmed = true""").findAll(labels).count() == 1 && labels.contains("TaskScopeRules.MAKE_SHARED_ASK"),
        )
        report.check(
            "lb1c: Settings > Labels shows this phone's labels with 'on <device>' and a scope switch; no screen offers removing a device",
            labels.contains("view.devicesLine(label.onlyOn, nameOf)") && labels.contains("actions.setLabelOnlyHere(label.id)") &&
                listOf(labels, picker, menu, bulk).none { Regex("""(?i)remove (the )?device|removeDevice""").containsMatchIn(it) },
        )
        report.check(
            "lb1c: the old pane is built through the scope (both builds), and its category list from the shown labels",
            Regex("""scope = scopeView\)""").findAll(screen).count() == 2 && cats.contains("TaskScopeView(store.lists.value, store.selfId().lowercase(), store.removed()).shown()"),
        )
        report.check("lb1c: the Archive shows no hidden label name", archive.contains("scopeView.labelShown(held)") && archive.contains("scopeView.categoryShown(it)"))
        report.check(
            "lb1c: every phone edit path is told the removed devices (the Tasks page's edits, drops, renames and adds; the Settings label edits; the old categories; the restore)",
            listOf(
                "ShelfEdits.editAll(lists, ids, field, self, now, removed(), change = change)", "ShelfEdits.edit(lists, id, field, self, now, removed(), change = change)",
                "ShelfEdits.dropOnPage(lists, ids, page, self, now, removed = removed())", "ShelfEdits.rename(lists, id, title, self, now, removed())", "removed = removed(), estimateMinutes = estimateMinutes",
                "TaskLabelEdits.add(lists, name, null, now, self, onlyOnThisDevice = onlyHere, removed = removed)", "ShelfEdits.editAll(added, forTasks, TaskField.LABEL, self, now, removed)",
            ).all { shelf.contains(it) } && actions.contains("store.removed()") && cats.contains("removedIds()") &&
                source("$app/tasks/PhoneTaskStore.kt").contains("removed = removed()") && source("$app/CompanionApp.kt").contains("taskModel.removedIds ="),
        )
        report.check("lb1c: the group heads carry the chosen icons (Devices for Shared, PhoneAndroid for This device)", picker.contains("Icons.Outlined.PhoneAndroid") && picker.contains("Icons.Outlined.Devices"))
    }
}
