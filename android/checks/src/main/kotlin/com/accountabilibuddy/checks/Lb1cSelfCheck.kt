package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.RunningSession
import com.accountabilibuddy.shared.SnapshotBuilder
import com.accountabilibuddy.shared.SnapshotInput
import com.accountabilibuddy.shared.WatchSnapshotJson
import com.accountabilibuddy.shared.goals.GoalLabelId
import com.accountabilibuddy.shared.goals.GoalSourceLines
import com.accountabilibuddy.shared.goals.GoalSourceRule
import com.accountabilibuddy.shared.goals.GoalSourceType
import com.accountabilibuddy.shared.sync.FieldStamp
import com.accountabilibuddy.shared.sync.SyncLaterItem
import com.accountabilibuddy.shared.sync.SessionTaskItem
import com.accountabilibuddy.shared.sync.SyncCategory
import com.accountabilibuddy.shared.sync.SyncNextTask
import com.accountabilibuddy.shared.sync.TaskLabels
import com.accountabilibuddy.shared.tasks.GoalLabelScopes
import com.accountabilibuddy.shared.tasks.OldPaneScopes
import com.accountabilibuddy.shared.tasks.OldTaskFields
import com.accountabilibuddy.shared.tasks.ScopeAsk
import com.accountabilibuddy.shared.tasks.TaskLocalEdits
import com.accountabilibuddy.shared.tasks.TaskReminder
import com.accountabilibuddy.shared.tasks.TaskReminderRings
import com.accountabilibuddy.shared.tasks.ViewFallback
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
import java.util.UUID

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

        // The watch: the REAL snapshot builder runs with a PC session whose task has the hidden category, then the real JSON writer.
        val session = SessionTaskItem(id(1), "Fix it", null, emptyList(), categoryId = work.id, category = "Work", categoryColor = "#2F6BEA")
        val running = RunningSession("Deep", Instant.parse("2026-11-12T11:00:00Z"), null, false).withTasks(listOf(session), null)
        val nowAt = Instant.parse("2026-11-12T12:00:00Z")
        val snapshot = SnapshotBuilder.build(SnapshotInput(nowAt, zone, true, "PC", emptyList(), null, running, runningAt = nowAt))
        val json = WatchSnapshotJson.write(snapshot).toString()
        report.check(
            "lb1c: the watch snapshot (built by SnapshotBuilder, written by WatchSnapshotJson) carries a running task's title and no label or tag name",
            snapshot.running != null && json.contains("Fix it") && !json.contains("Work") && !json.contains("secret") && !json.contains("#2F6BEA"), json.take(200),
        )

        reviewFixes(report)

        sourceChecks(report)
    }

    /**
     * LB1c review fixes, as behaviour on the real code: copies never join a hidden label or tag (through TaskLocalEdits.apply), the old pane's
     * detail source, Goals' label picker and source notes, the label name verdict, the default-view fallback, a phone on its own, the old
     * categories and presets, the reminders line, a typed tag that exists on a done task, the ask-first gate, and every edit path told the
     * removed devices (edit, editAll, drop, add).
     */
    private fun reviewFixes(report: CheckReport) {
        val v = view()
        val real = world()
        fun nameOf(id: String) = name(id)

        // 1. A copy (Move to Later, the Auto-saved handover, a restore of the old fields) never joins a hidden label or tag.
        val copy = SyncLaterItem(id(77), "Moved", null, "Work", "#2F6BEA", null, null, null, t0.plusMinutes(5).toInstant(), null, 9.0, labels = listOf("secret", "fresh"))
        val applied = TaskLocalEdits.apply(real, OldTaskFields(emptyList(), listOf(copy), emptyList(), emptyList(), emptyList(), emptyList()), PHONE, t0.plusMinutes(5), zone, removed = removed)
        val made = applied.tasks.firstOrNull { it.id == id(77) }?.task
        report.check("lb1c/copy: the copied task exists and keeps its hidden label and tag (nothing is dropped)", made != null && made.labelId == work.id && made.tags?.contains("secret") == true, made.toString())
        report.check(
            "lb1c/copy: the copy never joins the hidden label (Work stays on the work PC only) or the hidden tag (secret stays on the work PC only)",
            applied.labels.first { it.label.id == work.id }.label.onlyOn == listOf(PC) && TaskScopes.scopeOf("secret", applied.tagScopes)?.onlyOn == listOf(PC),
            applied.labels.first { it.label.id == work.id }.label.onlyOn.toString() + " " + TaskScopes.scopeOf("secret", applied.tagScopes)?.onlyOn,
        )
        val typed = ShelfEdits.add(real, id(78), "Typed", TaskBin.NEXT, null, listOf("secret"), PHONE, t0.plusMinutes(5), removed = removed).lists
        report.check("lb1c/copy: a tag the user TYPED (the Add sheet) still joins", TaskScopes.scopeOf("secret", typed.tagScopes)?.onlyOn == listOf(PC, PHONE).sorted())

        // 2. The old pane's task detail is built from the filtered lists.
        val next = SyncNextTask(id(1), "Fix it", null, "Work", "#2F6BEA", null, t0.toInstant(), null, 1.0, labels = listOf("secret", "plain"))
        val laterItem = SyncLaterItem(id(2), "Later one", null, "Work", "#2F6BEA", null, null, null, t0.toInstant(), null, 1.0)
        val running = SessionTaskItem(id(3), "Running", null, emptyList(), categoryId = work.id, category = "Work", categoryColor = "#2F6BEA")
        val source = OldPaneScopes.detailSource(listOf(next), listOf(laterItem), listOf(running), v)
        report.check(
            "lb1c/old pane: the task detail's source (Next, Later and the PC's running checklist) has no hidden category or colour",
            source.nextTasks.single().category == null && source.nextTasks.single().categoryColor == null && source.nextTasks.single().labels == listOf("plain") &&
                source.later.single().category == null && source.running.single().category == null && source.running.single().categoryColor == null,
        )
        val shownCat = OldPaneScopes.detailSource(listOf(next.copy(category = "Home")), emptyList(), emptyList(), v)
        report.check("lb1c/old pane: a shown category stays in the detail source", shownCat.nextTasks.single().category == "Home")
        report.check(
            "lb1c/old pane: a saved filter preset on a hidden category applies without it; a shown one stays",
            OldPaneScopes.presetCategory("Work", v) == null && OldPaneScopes.presetCategory("Home", v) == "Home" && OldPaneScopes.presetCategory(null, v) == null,
        )
        val listed = listOf(SyncCategory("Work", "#2F6BEA"), SyncCategory("Home", "#2F6BEA"), SyncCategory("Gym", "#2F6BEA"))
        val shownListed = OldPaneScopes.categories(listed, v).map { it.name }
        report.check("lb1c/old pane: the category list the create / rename checks run against has no hidden name", shownListed == listOf("Home", "Gym"), shownListed.toString())

        // 3. Goals: the label picker and the source note.
        val words = listOf("gym", "plain", "secret")
        val goalPicker = GoalLabelScopes.pickable(words, v)
        report.check("lb1c/goals: 'Focus on a label' offers no device-only word of another device (secret) and keeps the rest", goalPicker == listOf("gym", "plain"), goalPicker.toString())
        report.check("lb1c/goals: a hidden LABEL name (Work) is not offered either", GoalLabelScopes.pickable(listOf("Work", "Home"), v) == listOf("Home"))
        val hiddenId = GoalLabelId.idFor("secret")
        report.check(
            "lb1c/goals: a source on a hidden tag reads 'Uses a label from Work PC' (the device, never the name); a shown one has no note",
            GoalLabelScopes.hiddenNote(hiddenId, words, v, ::nameOf) == "Uses a label from Work PC" && GoalLabelScopes.hiddenNote(GoalLabelId.idFor("plain"), words, v, ::nameOf) == null &&
                GoalLabelScopes.hiddenNote(GoalLabelId.idFor("Work"), listOf("Work"), v, ::nameOf) == "Uses a label from Work PC",
        )
        val sourceGoal = Gs4SelfCheck.goal(1).copy(sources = listOf(GoalSourceRule(UUID.fromString("88888888-0000-0000-0000-000000000099"), GoalSourceType.FocusLabel, labelId = hiddenId)))
        val noName: (UUID) -> String? = { null }
        val line = GoalSourceLines.of(sourceGoal, null, noName) { GoalLabelScopes.hiddenNote(it, words, v, ::nameOf) }.first().text
        val plainLine = GoalSourceLines.of(sourceGoal, null, noName).first().text
        report.check("lb1c/goals: the goal page's source line says 'Uses a label from Work PC' and never the tag's name", line.startsWith("Uses a label from Work PC") && !line.contains("secret") && !plainLine.contains("secret"), line)
        report.check("lb1c/goals: counting is unchanged (the goal still holds the source on the hidden label)", sourceGoal.sources.single().labelId == hiddenId)

        // 4. Settings > Labels' name check runs against the full list.
        val rename = TaskScopeRules.labelNameVerdict(v, home.id, "Work", ::nameOf)
        report.check(
            "lb1c/settings: renaming a label to a name another device keeps hidden is refused with a note naming the device (not a silent no-op)",
            rename.state == TaskScopeRules.NameState.TakenElsewhere && !rename.saveable && rename.note == "'Work' is already a label on Work PC. Pick another name.", rename.note.orEmpty(),
        )
        val fresh = TaskScopeRules.labelNameVerdict(v, null, "work", ::nameOf)
        report.check(
            "lb1c/settings: a NEW label named like a hidden one may be added (it joins) and says so ('Work' is also on Work PC)",
            fresh.state == TaskScopeRules.NameState.JoinsElsewhere && fresh.saveable && fresh.note == TaskScopeRules.joinNote("work", listOf("Work PC")), fresh.note.orEmpty(),
        )
        report.check(
            "lb1c/settings: a shown duplicate is Taken; the label's own name, a free name and a blank are as before",
            TaskScopeRules.labelNameVerdict(v, null, "Home", ::nameOf).state == TaskScopeRules.NameState.Taken && TaskScopeRules.labelNameVerdict(v, home.id, "Home", ::nameOf).saveable &&
                TaskScopeRules.labelNameVerdict(v, null, "Brand new", ::nameOf).state == TaskScopeRules.NameState.Fine && TaskScopeRules.labelNameVerdict(v, null, "  ", ::nameOf).state == TaskScopeRules.NameState.Blank,
        )

        // 5. The default-view fallback.
        val hiddenOnly = TaskViewConfig.EVERYTHING.copy(labels = listOf(work.id))
        val fallback = ViewFallback.resolve(hiddenOnly, v, TaskViewConfig.EVERYTHING, ::nameOf)
        report.check("lb1c/view: a view that filters only by a hidden label opens Default with the note", fallback.fellBack && fallback.config == TaskViewConfig.EVERYTHING && fallback.note == "Uses a label from Work PC")
        report.check("lb1c/view: a hidden tag alone falls back too", ViewFallback.resolve(TaskViewConfig.EVERYTHING.copy(tags = listOf("secret")), v, TaskViewConfig.EVERYTHING, ::nameOf).fellBack)
        val mixed = ViewFallback.resolve(hiddenOnly.copy(labels = listOf(work.id, home.id)), v, TaskViewConfig.EVERYTHING, ::nameOf)
        report.check("lb1c/view: a view that also names a shown label keeps its own filter (no fallback)", !mixed.fellBack && mixed.config.labels == listOf(work.id, home.id))
        val timed = ViewFallback.resolve(hiddenOnly.copy(only = TaskViewConfig.EVERYTHING.only.copy(time = listOf("le25"))), v, TaskViewConfig.EVERYTHING, ::nameOf)
        report.check("lb1c/view: a view with another filter keeps it, and a view naming nothing hidden is untouched", !timed.fellBack && !ViewFallback.resolve(TaskViewConfig.EVERYTHING, v, TaskViewConfig.EVERYTHING, ::nameOf).fellBack)

        // 6. A phone that isn't in a group works alone: everything shows.
        val alone = TaskScopeView.forPhone(real, PHONE, joined = false, removed = removed)
        val together = TaskScopeView.forPhone(real, PHONE.uppercase(), joined = true, removed = removed)
        report.check(
            "lb1c/alone: a phone not in a group shows every label, tag and category; a joined one shows only its own",
            alone.labels.map { it.name }.toSet() == setOf("Work", "Home", "Gym", "Both", "Old") && alone.tagShown("secret") && alone.categoryShown("Work") && together.labels.map { it.name } == v.labels.map { it.name } && !together.tagShown("secret"),
        )

        // 7. The reminders line comes from the real list.
        val ringLabel = work.copy(ringOn = listOf("cccccccc-0000-0000-0000-00000000000c"))
        val withReminder = real.copy(
            labels = real.labels.map { if (it.label.id == work.id) SyncTaskLabel(ringLabel) else it },
            tasks = real.tasks.map { if (it.id == id(1)) it.copy(task = it.task.copy(reminders = listOf(TaskReminder("11111111-0000-0000-0000-000000000001", "at", t0)))) else it },
        )
        val rv = TaskScopeView(withReminder, PHONE, removed)
        val shownTask = rv.shown().tasks.first { it.id == id(1) }.task
        report.check(
            "lb1c/reminders: a task whose label is hidden here still says where it rings by that label's list (and never names the label)",
            shownTask.labelId == null && rv.ringsLine(shownTask, PHONE) == "Doesn't ring on this phone (Rings on)" && TaskReminderRings.line(rv.shown(), shownTask, PHONE) == "Rings on this phone",
            rv.ringsLine(shownTask, PHONE).orEmpty(),
        )

        // 8. A typed tag that exists only on a done task is not new.
        val doneTask = task(60, null, listOf("history")).let { it.copy(task = it.task.copy(doneAt = t0)) }
        val dv = TaskScopeView(real.copy(tasks = real.tasks + doneTask), PHONE, removed)
        report.check(
            "lb1c/add: a shared tag that exists only on a done task is not new (the Add sheet won't make it phone-only), a really new one is, and a hidden one exists",
            dv.tagExists("history") && dv.tagExists("HISTORY") && !dv.tagExists("brand-new") && dv.tagExists("secret"),
        )

        // 9. The ask-first gate.
        val idle = ScopeAsk<String>()
        report.check("lb1c/gate: nothing asked, nothing to confirm: a tap alone never acts", !idle.asking && idle.confirmed() == null)
        val asked = idle.ask("both")
        report.check("lb1c/gate: asking opens the dialog on that target; confirming acts on it; dismissing clears it", asked.asking && asked.confirmed() == "both" && !asked.dismissed().asking && asked.dismissed().confirmed() == null)

        // 10. Every edit path is told the removed devices (a removed device's label can be taken off), also the bulk, drop and add paths.
        fun label4(l: TaskLists) = l.tasks.first { it.id == id(4) }.task.labelId
        val bulk = ShelfEdits.editAll(real, listOf(id(4)), TaskField.LABEL, PHONE, t0.plusMinutes(1), removed) { it.copy(labelId = null) }.lists
        val bulkBlind = ShelfEdits.editAll(real, listOf(id(4)), TaskField.LABEL, PHONE, t0.plusMinutes(1), null) { it.copy(labelId = null) }.lists
        report.check("lb1c/removed: the bulk edit told the removed devices takes a removed device's label off; told none it keeps it", label4(bulk) == null && label4(bulkBlind) == old.id)
        val pages = ShelfPages.build(v.shown(), TaskViewConfig.EVERYTHING.copy(group = TaskViewConfig.GROUP_LABEL), t0, zone)
        val none = pages.first { it.isNone }
        val dropTold = ShelfEdits.dropOnPage(real, listOf(id(4)), none, PHONE, t0.plusMinutes(1), removed = removed).lists
        val dropBlind = ShelfEdits.dropOnPage(real, listOf(id(4)), none, PHONE, t0.plusMinutes(1), removed = null).lists
        report.check("lb1c/removed: dropping on No label, told the removed devices, takes a removed device's label off; told none it keeps it", label4(dropTold) == null && label4(dropBlind) == old.id)
        val addTold = ShelfEdits.add(real, id(91), "Typed", TaskBin.NEXT, old.id, null, PHONE, t0.plusMinutes(1), removed = removed).lists
        val addBlind = ShelfEdits.add(real, id(92), "Typed", TaskBin.NEXT, old.id, null, PHONE, t0.plusMinutes(1), removed = null).lists
        report.check(
            "lb1c/removed: the Add sheet's pick of a removed device's label joins nothing when told the removed devices, and joins when not told",
            addTold.labels.first { it.label.id == old.id }.label.onlyOn == listOf(GONE) && addBlind.labels.first { it.label.id == old.id }.label.onlyOn == listOf(GONE, PHONE).sorted(),
            addTold.labels.first { it.label.id == old.id }.label.onlyOn.toString(),
        )
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
            "lb1c: every new-label form is the scoped one (shared by default, an 'Only on this phone' switch)",
            host.contains("NewLabelForm(scopeView, canKeepHere") && menu.contains("NewLabelForm(scopeView, canKeepHere") && bulk.contains("NewLabelForm(scopeView, canKeepHere") && picker.contains("TaskScopeRules.ONLY_ON_THIS_PHONE"),
        )
        report.check(
            "lb1c: the Add sheet offers device-only labels after the shared ones and an 'Only on this phone' choice for new tags",
            add.contains(".sortedBy { it.id in deviceLabelIds }") && add.contains("onlyHereTags") && host.contains("controller.setTagScope(it, onlyHere = true)"),
        )
        report.check(
            "lb1c: Settings > Labels shows this phone's labels with 'on <device>', asks before a scope change; no screen offers removing a device",
            labels.contains("view.devicesLine(label.onlyOn, nameOf)") && labels.contains("onlyHereAsk.confirmed()") &&
                listOf(labels, picker, menu, bulk).none { Regex("""(?i)remove (the )?device|removeDevice""").containsMatchIn(it) },
        )
        report.check("lb1c: the Archive shows no hidden label name", archive.contains("scopeView.labelShown(held)") && archive.contains("scopeView.categoryShown(it)"))
        report.check("lb1c: the group heads carry the chosen icons (Devices for Shared, PhoneAndroid for This device)", picker.contains("Icons.Outlined.PhoneAndroid") && picker.contains("Icons.Outlined.Devices"))

        // Every read of a raw label or tag list in ui/ goes through TaskScopeView (shown()/labels/...) or is allow-listed here with its reason.
        val patterns = listOf(
            Regex("""allLabels\(\)"""), Regex("""\.labels\.map \{"""), Regex("""lists\.labels"""), Regex("""taskModel\.lists"""), Regex("""store\.lists"""),
            Regex("""TaskLabelEdits\.named\("""), Regex("""sharedLists\.(nextTasks|later|parked|autoSaved)"""), Regex("""\.tagScopes"""),
        )
        val allowed = mapOf(
            "ui/goals/GoalEditorSheets.kt" to "allLabels() goes through GoalLabelScopes (picker and source note)",
            "ui/goals/GoalPageState.kt" to "allLabels() goes through GoalLabelScopes (the page's names and source note)",
            "ui/settings/TaskLabelsPage.kt" to "lists here is scopeView.shown(); the name check is TaskScopeRules.labelNameVerdict over the full view",
            "ui/tasks/page/SessionFullSheet.kt" to "lists is the Tasks page's scopeView.shown() (passed in)",
            "ui/tasks/page/ShelfController.kt" to "writers act on the store's real list (they keep what is hidden), not on screens' reads",
            "ui/tasks/page/TasksPageHost.kt" to "lists is scopeView.shown(); the other reads are counts, the zone and the sort request",
            "ui/tasks/ScopeViews.kt" to "the one place the scope view is built from the raw list",
            "ui/tasks/TasksController.kt" to "categories come through OldPaneScopes.categories over app.taskModel.scopeView()",
            "ui/TasksScreen.kt" to "the old pane: raw lists only feed OldPaneScopes / TasksModel.build(scope =) / detailSource",
            "ui/CompanionViewModel.kt" to "taskLists feed counts and the strip, never a label's name",
            "ui/PhoneToday.kt" to "hasTodayTasks counts tasks; Today's labels come from TodayTaskSections' scopeView",
            "ui/settings/TaskRemindersCard.kt" to "the account's rings list, no label names",
            "ui/settings/TasksSettingsPage.kt" to "view names and the account's default view; the label count goes through the scope view",
            "ui/TasksSetupPages.kt" to "view names and the account's rings, no label names",
            "ui/tasks/archive/ArchiveData.kt" to "builds its own scopeView from the raw list, shows names only through it",
            "ui/MainScreen.kt" to "taskModel.earlierNotice / requestSort, no lists",
            "ui/today/TodayTaskSections.kt" to "the zone only; the list is scopeView.shown()",
        )
        val uiDir = File("$app/ui")
        val found = uiDir.walkTopDown().filter { it.isFile && it.extension == "kt" }.map { file -> file.relativeTo(File(app)).path.replace(File.separatorChar, '/') to file.readText().replace("\r\n", "\n") }.toList()
        val offenders = found.filter { (path, text) -> patterns.any { it.containsMatchIn(text) } && path !in allowed }.map { it.first }
        report.check("lb1c/scan: every read of a raw label or tag list in ui/ goes through TaskScopeView or is allow-listed with a reason", found.isNotEmpty() && offenders.isEmpty(), offenders.toString())
        val goalFiles = found.filter { (_, text) -> Regex("""allLabels\(\)""").containsMatchIn(text) }
        report.check("lb1c/scan: allLabels() in ui/ is only read next to GoalLabelScopes", goalFiles.all { it.second.contains("GoalLabelScopes") }, goalFiles.map { it.first }.toString())
        val gateless = found.filter { (path, text) -> path != "ui/tasks/page/ShelfController.kt" && Regex("""confirmed = true""").containsMatchIn(text) && !text.contains(".confirmed()") }.map { it.first }
        report.check("lb1c/scan: every 'confirmed = true' in ui/ sits in a screen that asks through ScopeAsk first (ShelfController's createLabel is a writer: only NewLabelForm's dialog passes makeShared)", gateless.isEmpty(), gateless.toString())
    }
}
