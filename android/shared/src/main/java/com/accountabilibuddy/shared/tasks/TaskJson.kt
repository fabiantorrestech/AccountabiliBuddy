package com.accountabilibuddy.shared.tasks

import com.accountabilibuddy.shared.goals.GoalJson
import com.accountabilibuddy.shared.sync.DotNetTime
import com.accountabilibuddy.shared.sync.FieldStamp
import com.accountabilibuddy.shared.sync.TaskPlannedFor
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonObjectBuilder
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.put
import java.time.LocalDate
import java.time.LocalTime
import java.time.OffsetDateTime
import java.time.format.DateTimeParseException

/**
 * ADR 76's task parts as JSON, in the PC's shape (Core's records through System.Text.Json with `SyncJson.Options`): the sync
 * file's `tasks`, `labels`, `views`, `taskLog` and `taskAccountSettings`. The same camelCase names in the same order, an
 * optional field left out when it is null (a flag when it is false, a snooze count when it is 0), enums by their lower-case names,
 * whole numbers without a fraction, dates "yyyy-MM-dd", times of day "HH:mm:ss", times as .NET writes a DateTimeOffset with the
 * offset kept, ids in lower case. [text] gives the PC's bytes, so a task read from the PC and written back is the same text.
 *
 * Reading takes what the PC's reader takes (names in any case, a number written as text, a missing field as its default) and,
 * like the PC's lenient lists and `SyncFileCodec`'s cleaning, leaves out an entry it can't use (no id, no title on a live task, no
 * name, no view settings, no snapshot, a value of the wrong kind); leaving one out never deletes it, the merge keeps what it has.
 *
 * Beyond the PC (which drops both): a field this version doesn't know is kept in the object's `extra` and written back after the
 * known fields when `unknown` is true (the stores and files; false gives the PC's own shape, for tie-breaks and comparisons), and
 * an entry holding an enum value this version doesn't know (a newer app's bin, kind or status) is handed back as written in
 * [TaskRead.newer] instead of being lost. Unknown fields of a planned day part are not kept.
 */
object TaskJson {
    private const val EMPTY_ID = FieldStamp.NOBODY

    // System.Text.Json reads a Guid only in its 36-character form.
    private val guidForm = Regex("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")

    // The PC's computed properties ([JsonIgnore]): never written by it, dropped when read.
    private val TASK_COMPUTED = arrayOf("isLive", "isDone", "localTo", "actionCount")
    private val STEP_COMPUTED = arrayOf("isDone")
    private val LIVE_COMPUTED = arrayOf("isLive")
    private val STAMPS_COMPUTED = arrayOf("newest")
    private val ITEM_COMPUTED = arrayOf("id", "editedAt", "deletedAt", "position", "localTo", "tieBreak")
    private val ACCOUNT_COMPUTED = arrayOf("isEmpty", "editedAt")
    private val NOTHING_COMPUTED = emptyArray<String>()

    /** Compact JSON text exactly as the PC writes it, byte for byte ([GoalJson.text]). */
    fun text(json: JsonElement): String = GoalJson.text(json)

    // ---------------------------------------------------------------- writing

    fun writeTask(task: TaskItem, unknown: Boolean = true): JsonObject = buildJsonObject {
        put("id", task.id)
        put("title", task.title)
        put("bin", task.bin.wire)
        put("position", GoalJson.number(task.position))
        put("createdAt", GoalJson.timeValue(task.createdAt))
        put("editedAt", GoalJson.timeValue(task.editedAt))
        put("touchedAt", GoalJson.timeValue(task.touchedAt))
        task.number?.let { put("number", writeNumber(it, unknown)) }
        task.formerNumbers?.let { numbers -> put("formerNumbers", JsonArray(numbers.map { JsonPrimitive(it) })) }
        task.prevBin?.let { put("prevBin", it.wire) }
        task.labelId?.let { put("label", it) }
        task.tags?.let { put("tags", texts(it)) }
        task.steps?.let { put("steps", writeSteps(it, unknown)) }
        task.estimateMinutes?.let { put("estimateMinutes", it) }
        task.effort?.let { put("effort", it) }
        task.note?.let { put("note", it) }
        task.dueOn?.let { put("dueOn", it.toString()) }
        task.dueAt?.let { put("dueAt", GoalJson.timeOfDay(it)) }
        task.reminders?.let { put("reminders", writeReminders(it, unknown)) }
        if (task.cantMiss) put("cantMiss", true)
        if (task.highlight) put("highlight", true)
        task.origin?.let { put("origin", it) }
        task.doneAt?.let { put("doneAt", GoalJson.timeValue(it)) }
        task.devices?.let { put("devices", texts(it)) }
        if (task.calendar) put("calendar", true)
        task.plannedFor?.let { put("plannedFor", writePlannedFor(it)) }
        task.deletedAt?.let { put("deletedAt", GoalJson.timeValue(it)) }
        task.conflicts?.let { conflicts -> put("conflicts", JsonArray(conflicts.map { writeConflict(it, unknown) })) }
        task.autoSavedFor?.let { put("autoSavedFor", it.toString()) }
        putExtra(task.extra, unknown)
    }

    fun writeNumber(number: TaskNumber, unknown: Boolean = true): JsonObject = buildJsonObject {
        put("value", number.value)
        if (number.temporary) put("temporary", true)
        putExtra(number.extra, unknown)
    }

    fun writeSteps(steps: List<TaskStep>, unknown: Boolean = true): JsonArray = JsonArray(steps.map { step ->
        buildJsonObject {
            put("id", step.id)
            put("title", step.title)
            step.doneAt?.let { put("doneAt", GoalJson.timeValue(it)) }
            if (step.focus) put("focus", true)
            step.estimateMinutes?.let { put("estimateMinutes", it) }
            putExtra(step.extra, unknown)
        }
    })

    fun writeReminders(reminders: List<TaskReminder>, unknown: Boolean = true): JsonArray = JsonArray(reminders.map { reminder ->
        buildJsonObject {
            put("id", reminder.id)
            put("rule", reminder.rule)
            reminder.at?.let { put("at", GoalJson.timeValue(it)) }
            reminder.occurrences?.let { occurrences -> put("occurrences", JsonArray(occurrences.map { writeOccurrence(it, unknown) })) }
            reminder.calendarEventId?.let { put("calendarEventId", it) }
            putExtra(reminder.extra, unknown)
        }
    })

    private fun writeOccurrence(occurrence: TaskReminderOccurrence, unknown: Boolean): JsonObject = buildJsonObject {
        put("due", GoalJson.timeValue(occurrence.due))
        put("status", occurrence.status.wire)
        occurrence.snoozedUntil?.let { put("snoozedUntil", GoalJson.timeValue(it)) }
        if (occurrence.snoozeCount != 0) put("snoozeCount", occurrence.snoozeCount)
        putExtra(occurrence.extra, unknown)
    }

    private fun writeConflict(conflict: TaskConflict, unknown: Boolean): JsonObject = buildJsonObject {
        put("field", conflict.field)
        put("otherValue", conflict.otherValue)
        put("otherDeviceId", conflict.otherDeviceId)
        put("otherEditedAt", GoalJson.timeValue(conflict.otherEditedAt))
        putExtra(conflict.extra, unknown)
    }

    private fun writePlannedFor(slot: TaskPlannedFor): JsonObject = buildJsonObject {
        put("date", slot.date)
        put("partId", slot.partId)
        put("start", slot.start)
    }

    fun writeStamp(stamp: FieldStamp, unknown: Boolean = true): JsonObject = buildJsonObject {
        put("at", GoalJson.timeValue(stamp.at))
        put("by", stamp.by)
        stamp.bases?.let { put("bases", texts(it)) }
        putExtra(stamp.extra, unknown)
    }

    fun writeStamps(stamps: TaskFieldStamps, unknown: Boolean = true): JsonObject = buildJsonObject {
        for (field in TaskField.entries) {
            stamps.of(field)?.let { put(field.wire, writeStamp(it, unknown)) }
        }

        putExtra(stamps.extra, unknown)
    }

    fun writeSyncTask(item: SyncTaskItem, unknown: Boolean = true): JsonObject = buildJsonObject {
        put("task", writeTask(item.task, unknown))
        put("stamps", writeStamps(item.stamps, unknown))
        putExtra(item.extra, unknown)
    }

    fun writeLabel(label: TaskLabel, unknown: Boolean = true): JsonObject = buildJsonObject {
        put("id", label.id)
        put("name", label.name)
        put("color", label.color)
        put("position", GoalJson.number(label.position))
        put("editedAt", GoalJson.timeValue(label.editedAt))
        label.ringOn?.let { put("ringOn", texts(it)) }
        label.deletedAt?.let { put("deletedAt", GoalJson.timeValue(it)) }
        label.ringOnStamp?.let { put("ringOnStamp", writeStamp(it, unknown)) }
        label.onlyOn?.let { put("onlyOn", texts(it)) }
        label.onlyOnStamp?.let { put("onlyOnStamp", writeStamp(it, unknown)) }
        putExtra(label.extra, unknown)
    }

    /** One tag scope (ADR 89) in the PC's shape: id, name, onlyOn, editedAt, deletedAt; a tombstone has no name or devices. */
    fun writeTagScope(scope: SyncTagScope): JsonObject = buildJsonObject {
        put("id", scope.id)
        scope.name?.let { put("name", it) }
        scope.onlyOn?.let { put("onlyOn", texts(it)) }
        put("editedAt", GoalJson.timeValue(scope.edited))
        scope.deleted?.let { put("deletedAt", GoalJson.timeValue(it)) }
    }

    fun writeTagScopes(items: List<SyncTagScope>): JsonArray = JsonArray(items.map(::writeTagScope))

    fun writeSyncLabel(item: SyncTaskLabel, unknown: Boolean = true): JsonObject = buildJsonObject {
        put("label", writeLabel(item.label, unknown))
        putExtra(item.extra, unknown)
    }

    fun writeFilter(filter: TaskViewFilter, unknown: Boolean = true): JsonObject = buildJsonObject {
        filter.due?.let { put("due", texts(it)) }
        filter.time?.let { put("time", texts(it)) }
        filter.old?.let { put("old", texts(it)) }
        if (filter.highlightedOnly) put("highlightedOnly", true)
        putExtra(filter.extra, unknown)
    }

    fun writeConfig(config: TaskViewConfig, unknown: Boolean = true): JsonObject = buildJsonObject {
        put("only", writeFilter(config.only, unknown))
        config.labels?.let { put("labels", texts(it)) }
        if (config.noLabel) put("noLabel", true)
        config.tags?.let { put("tags", texts(it)) }
        put("match", config.match)
        put("group", config.group)
        put("sort", config.sort)
        if (config.pinHighlights) put("pinHighlights", true)
        put("density", config.density)
        config.bins?.let { bins -> put("bins", JsonArray(bins.map { JsonPrimitive(it.wire) })) }
        config.seed?.let { put("seed", it) }
        putExtra(config.extra, unknown)
    }

    fun writeView(view: TaskView, unknown: Boolean = true): JsonObject = buildJsonObject {
        put("id", view.id)
        put("name", view.name)
        view.icon?.let { put("icon", it) }
        put("cfg", writeConfig(view.config, unknown))
        put("position", GoalJson.number(view.position))
        put("editedAt", GoalJson.timeValue(view.editedAt))
        if (view.builtIn) put("builtIn", true)
        view.deletedAt?.let { put("deletedAt", GoalJson.timeValue(it)) }
        putExtra(view.extra, unknown)
    }

    fun writeSyncView(item: SyncTaskView, unknown: Boolean = true): JsonObject = buildJsonObject {
        put("view", writeView(item.view, unknown))
        putExtra(item.extra, unknown)
    }

    fun writeEntry(entry: TaskHistoryEntry, unknown: Boolean = true): JsonObject = buildJsonObject {
        put("id", entry.id)
        put("kind", entry.kind.wire)
        put("when", GoalJson.timeValue(entry.at))
        put("task", writeTask(entry.task, unknown))
        put("editedAt", GoalJson.timeValue(entry.editedAt))
        entry.deletedAt?.let { put("deletedAt", GoalJson.timeValue(it)) }
        putExtra(entry.extra, unknown)
    }

    fun writeSyncLogItem(item: SyncTaskLogItem, unknown: Boolean = true): JsonObject = buildJsonObject {
        put("entry", writeEntry(item.entry, unknown))
        putExtra(item.extra, unknown)
    }

    fun writeAccountSettings(settings: SyncTaskAccountSettings, unknown: Boolean = true): JsonObject = buildJsonObject {
        settings.defaultViewId?.let { put("defaultViewId", it) }
        settings.defaultViewStamp?.let { put("defaultViewStamp", writeStamp(it, unknown)) }
        settings.pickedCalendarId?.let { put("pickedCalendarId", it) }
        settings.pickedCalendarStamp?.let { put("pickedCalendarStamp", writeStamp(it, unknown)) }
        settings.ringOn?.let { put("ringOn", texts(it)) }
        settings.ringOnStamp?.let { put("ringOnStamp", writeStamp(it, unknown)) }
        putExtra(settings.extra, unknown)
    }

    /** The sync file's `tasks` (or a store's list): [items], then the entries of a newer app kept as written. */
    fun writeTasks(items: List<SyncTaskItem>, newer: List<JsonObject> = emptyList()): JsonArray = JsonArray(items.map { writeSyncTask(it) } + newer)

    fun writeLabels(items: List<SyncTaskLabel>, newer: List<JsonObject> = emptyList()): JsonArray = JsonArray(items.map { writeSyncLabel(it) } + newer)

    fun writeViews(items: List<SyncTaskView>, newer: List<JsonObject> = emptyList()): JsonArray = JsonArray(items.map { writeSyncView(it) } + newer)

    fun writeTaskLog(items: List<SyncTaskLogItem>, newer: List<JsonObject> = emptyList()): JsonArray = JsonArray(items.map { writeSyncLogItem(it) } + newer)

    // ---------------------------------------------------------------- reading the parts (lenient, cleaned)

    /** The `tasks` part; null when it isn't an array (an app that predates it). */
    fun readTasks(json: JsonElement?): TaskRead<SyncTaskItem>? = readList(json) { item -> readSyncTask(item).takeIf { isUsable(it.task) } }

    /** The `labels` part: a colour that can't be read becomes the set's first, and "Rings on" is cleaned. */
    fun readLabels(json: JsonElement?): TaskRead<SyncTaskLabel>? = readList(json) { item ->
        val read = readSyncLabel(item)
        if (read.label.id == EMPTY_ID) null else cleanOnlyOn(cleanRingOn(read))
    }

    /** The `tagScopes` part (ADR 89), leniently and cleaned ([TaskScopes.clean]); null when it isn't an array. */
    fun readTagScopes(json: JsonElement?): List<SyncTagScope>? =
        readList(json) { item -> readSyncTagScope(item) }?.let { TaskScopes.clean(it.items) }

    fun readViews(json: JsonElement?): TaskRead<SyncTaskView>? = readList(json) { item -> readSyncView(item).takeIf { it.view.id != EMPTY_ID } }

    fun readTaskLog(json: JsonElement?): TaskRead<SyncTaskLogItem>? =
        readList(json) { item -> readSyncLogItem(item).takeIf { it.entry.id != EMPTY_ID && isUsable(it.entry.task) } }

    /** The `taskAccountSettings` part, cleaned ([TaskAccountSettingsMerge.clean]); null when it is missing or can't be read. */
    fun readAccountSettings(json: JsonElement?): SyncTaskAccountSettings? {
        val element = json as? JsonObject ?: return null
        val read = try {
            readAccountSettingsObject(element)
        } catch (e: IllegalArgumentException) {
            return null
        }

        return TaskAccountSettingsMerge.clean(read)
    }

    /** The five parts of a sync file (or of a store's document holding the same names). */
    fun readParts(file: JsonObject): TaskParts {
        val tasks = readTasks(file["tasks"])
        val labels = readLabels(file["labels"])
        val views = readViews(file["views"])
        val log = readTaskLog(file["taskLog"])
        return TaskParts(
            tasks?.items, labels?.items, views?.items, log?.items, readAccountSettings(file["taskAccountSettings"]),
            tasks?.newer.orEmpty(), labels?.newer.orEmpty(), views?.newer.orEmpty(), log?.newer.orEmpty(),
            readTagScopes(file["tagScopes"]),
        )
    }

    /**
     * The parts as the file's fields: a null part is left out, as the PC leaves it out. [withScopes]: ADR 89's tag scopes too (a store's
     * document holds them; a sync file writes them last, after the other parts, as the PC's file does).
     */
    fun writeParts(parts: TaskParts, withScopes: Boolean = true): JsonObject = buildJsonObject {
        parts.tasks?.let { put("tasks", writeTasks(it, parts.newerTasks)) }
        parts.labels?.let { put("labels", writeLabels(it, parts.newerLabels)) }
        parts.views?.let { put("views", writeViews(it, parts.newerViews)) }
        parts.taskLog?.let { put("taskLog", writeTaskLog(it, parts.newerTaskLog)) }
        parts.accountSettings?.let { put("taskAccountSettings", writeAccountSettings(it)) }
        if (withScopes) parts.tagScopes?.let { put("tagScopes", writeTagScopes(it)) }
    }

    private fun <T> readList(json: JsonElement?, read: (JsonObject) -> T?): TaskRead<T>? {
        val array = json as? JsonArray ?: return null
        val items = ArrayList<T>(array.size)
        val newer = ArrayList<JsonObject>()
        for (element in array) {
            val item = element as? JsonObject ?: continue
            try {
                read(item)?.let { items += it }
            } catch (e: TaskNewerValueException) {
                newer += item
            } catch (e: IllegalArgumentException) {
                // An entry this app can't read is left out.
            }
        }

        return TaskRead(items, newer)
    }

    private fun isUsable(task: TaskItem): Boolean = task.id != EMPTY_ID

    // A label's rings on as the account's: no empty or repeated ids, in id order; too many drops the list and its stamp.
    private fun cleanRingOn(item: SyncTaskLabel): SyncTaskLabel {
        val ringOn = TaskRingDevices.normalized(item.label.ringOn) ?: return item
        return if (ringOn.size > TaskRingDevices.MAX_DEVICES) {
            item.copy(label = item.label.copy(ringOn = null, ringOnStamp = null))
        } else {
            item.copy(label = item.label.copy(ringOn = ringOn))
        }
    }

    // A label's "only on" (ADR 89) as its rings on: no empty or repeated ids, in id order (none left is shared); too many drops the list and its stamp.
    private fun cleanOnlyOn(item: SyncTaskLabel): SyncTaskLabel {
        val onlyOn = item.label.onlyOn ?: return item
        val devices = TaskScopes.normalized(onlyOn)
        return if (devices != null && devices.size > TaskRingDevices.MAX_DEVICES) {
            item.copy(label = item.label.copy(onlyOn = null, onlyOnStamp = null))
        } else {
            item.copy(label = item.label.copy(onlyOn = devices))
        }
    }

    // ---------------------------------------------------------------- reading one object (throws on damage)

    /** A `tasks` entry. Throws [IllegalArgumentException] for one that can't be used, [TaskNewerValueException] for a newer app's value. */
    fun readSyncTask(json: JsonElement): SyncTaskItem {
        val fields = Fields(obj(json, "task entry"))
        val task = readTask(fields.element("task") ?: throw missing("task"))
        val stamps = fields.element("stamps")?.let(::readStamps) ?: TaskFieldStamps.NONE
        return SyncTaskItem(task, stamps, fields.extra(ITEM_COMPUTED))
    }

    fun readTask(json: JsonElement): TaskItem {
        val fields = Fields(obj(json, "task"))
        val id = fields.guid("id") ?: EMPTY_ID
        val title = fields.text("title")
        val bin = fields.enumOf("bin", TaskBin.entries) { it.wire } ?: TaskBin.SESSION
        val position = fields.double("position") ?: 0.0
        val createdAt = fields.time("createdAt") ?: DotNetTime.MIN
        val editedAt = fields.time("editedAt") ?: DotNetTime.MIN
        val touchedAt = fields.time("touchedAt") ?: DotNetTime.MIN
        val number = fields.element("number")?.let(::readNumber)
        val formerNumbers = fields.array("formerNumbers")?.map { intOf(it, "formerNumbers") }
        val prevBin = fields.enumOf("prevBin", TaskBin.entries) { it.wire }
        val labelId = fields.guid("label")
        val tags = fields.texts("tags")
        val steps = fields.array("steps")?.map(::readStep)
        val estimateMinutes = fields.int("estimateMinutes")
        val effort = fields.int("effort")
        val note = fields.text("note")
        val dueOn = fields.date("dueOn")
        val dueAt = fields.timeOfDay("dueAt")
        val reminders = fields.array("reminders")?.map(::readReminder)
        val cantMiss = fields.bool("cantMiss") ?: false
        val highlight = fields.bool("highlight") ?: false
        val origin = fields.text("origin")
        val doneAt = fields.time("doneAt")
        val devices = fields.guids("devices")
        val calendar = fields.bool("calendar") ?: false
        val plannedFor = fields.element("plannedFor")?.let(::readPlannedFor)
        val deletedAt = fields.time("deletedAt")
        val conflicts = fields.array("conflicts")?.map(::readConflict)
        val autoSavedFor = fields.date("autoSavedFor")
        if (title == null && deletedAt == null) {
            throw missing("title")
        }

        return TaskItem(
            id, title ?: "", bin, position, createdAt, editedAt, touchedAt, number, formerNumbers, prevBin, labelId, tags, steps,
            estimateMinutes, effort, note, dueOn, dueAt, reminders, cantMiss, highlight, origin, doneAt, devices, calendar, plannedFor,
            deletedAt, conflicts, autoSavedFor, fields.extra(TASK_COMPUTED),
        )
    }

    private fun readNumber(json: JsonElement): TaskNumber {
        val fields = Fields(obj(json, "number"))
        return TaskNumber(fields.int("value") ?: 0, fields.bool("temporary") ?: false, fields.extra(NOTHING_COMPUTED))
    }

    private fun readStep(json: JsonElement): TaskStep {
        val fields = Fields(obj(json, "step"))
        return TaskStep(
            fields.guid("id") ?: EMPTY_ID, fields.text("title") ?: throw missing("step title"), fields.time("doneAt"), fields.bool("focus") ?: false,
            fields.int("estimateMinutes"), fields.extra(STEP_COMPUTED),
        )
    }

    private fun readReminder(json: JsonElement): TaskReminder {
        val fields = Fields(obj(json, "reminder"))
        return TaskReminder(
            fields.guid("id") ?: EMPTY_ID, fields.text("rule") ?: throw missing("reminder rule"), fields.time("at"),
            fields.array("occurrences")?.map(::readOccurrence), fields.text("calendarEventId"), fields.extra(NOTHING_COMPUTED),
        )
    }

    private fun readOccurrence(json: JsonElement): TaskReminderOccurrence {
        val fields = Fields(obj(json, "occurrence"))
        return TaskReminderOccurrence(
            fields.time("due") ?: DotNetTime.MIN, fields.enumOf("status", TaskReminderStatus.entries) { it.wire } ?: TaskReminderStatus.UP,
            fields.time("snoozedUntil"), fields.int("snoozeCount") ?: 0, fields.extra(NOTHING_COMPUTED),
        )
    }

    private fun readConflict(json: JsonElement): TaskConflict {
        val fields = Fields(obj(json, "conflict"))
        return TaskConflict(
            fields.text("field") ?: throw missing("conflict field"), fields.text("otherValue"), fields.guid("otherDeviceId") ?: EMPTY_ID,
            fields.time("otherEditedAt") ?: DotNetTime.MIN, fields.extra(NOTHING_COMPUTED),
        )
    }

    // The day part in the form the PC writes it back: the date as "yyyy-MM-dd", the part id in lower case, the start as a TimeOnly.
    private fun readPlannedFor(json: JsonElement): TaskPlannedFor {
        val fields = Fields(obj(json, "plannedFor"))
        val date = fields.date("date") ?: LocalDate.of(1, 1, 1)
        val start = fields.timeOfDay("start") ?: LocalTime.MIDNIGHT
        return TaskPlannedFor(date.toString(), fields.guid("partId") ?: EMPTY_ID, GoalJson.timeOfDay(start))
    }

    fun readStamp(json: JsonElement): FieldStamp {
        val fields = Fields(obj(json, "stamp"))
        return FieldStamp(fields.time("at") ?: DotNetTime.MIN, fields.guid("by") ?: EMPTY_ID, fields.texts("bases"), fields.extra(NOTHING_COMPUTED))
    }

    fun readStamps(json: JsonElement): TaskFieldStamps {
        val fields = Fields(obj(json, "stamps"))
        var stamps = TaskFieldStamps.NONE
        for (field in TaskField.entries) {
            fields.element(field.wire)?.let { stamps = stamps.with(field, readStamp(it)) }
        }

        return stamps.copy(extra = fields.extra(STAMPS_COMPUTED))
    }

    fun readSyncLabel(json: JsonElement): SyncTaskLabel {
        val fields = Fields(obj(json, "label entry"))
        val label = readLabel(fields.element("label") ?: throw missing("label"))
        return SyncTaskLabel(label, fields.extra(ITEM_COMPUTED))
    }

    fun readLabel(json: JsonElement): TaskLabel {
        val fields = Fields(obj(json, "label"))
        val id = fields.guid("id") ?: EMPTY_ID
        val name = fields.text("name") ?: throw missing("label name")
        val color = fields.text("color")
        return TaskLabel(
            id, name, if (TaskLabelColors.isValid(color)) color!! else TaskLabelColors.SET[0], fields.double("position") ?: 0.0,
            fields.time("editedAt") ?: DotNetTime.MIN, fields.guids("ringOn"), fields.time("deletedAt"), fields.element("ringOnStamp")?.let(::readStamp),
            fields.guids("onlyOn"), fields.element("onlyOnStamp")?.let(::readStamp),
            fields.extra(LIVE_COMPUTED),
        )
    }

    /** A `tagScopes` entry (ADR 89). Throws [IllegalArgumentException] for one that can't be used. */
    fun readSyncTagScope(json: JsonElement): SyncTagScope {
        val fields = Fields(obj(json, "tag scope"))
        return SyncTagScope(
            fields.guid("id") ?: EMPTY_ID, fields.text("name"), fields.guids("onlyOn"), fields.time("editedAt") ?: DotNetTime.MIN, fields.time("deletedAt"),
        )
    }

    fun readSyncView(json: JsonElement): SyncTaskView {
        val fields = Fields(obj(json, "view entry"))
        val view = readView(fields.element("view") ?: throw missing("view"))
        return SyncTaskView(view, fields.extra(ITEM_COMPUTED))
    }

    fun readView(json: JsonElement): TaskView {
        val fields = Fields(obj(json, "view"))
        return TaskView(
            fields.guid("id") ?: EMPTY_ID, fields.text("name") ?: throw missing("view name"), fields.text("icon"),
            readConfig(fields.element("cfg") ?: throw missing("cfg")), fields.double("position") ?: 0.0, fields.time("editedAt") ?: DotNetTime.MIN,
            fields.bool("builtIn") ?: false, fields.time("deletedAt"), fields.extra(LIVE_COMPUTED),
        )
    }

    fun readConfig(json: JsonElement): TaskViewConfig {
        val fields = Fields(obj(json, "cfg"))
        return TaskViewConfig(
            readFilter(fields.element("only") ?: throw missing("only")),
            fields.guids("labels"),
            fields.bool("noLabel") ?: false,
            fields.texts("tags"),
            fields.text("match") ?: TaskViewConfig.MATCH_ANY,
            fields.text("group") ?: TaskViewConfig.GROUP_BINS,
            fields.text("sort") ?: TaskViewConfig.SORT_DEFAULT,
            fields.bool("pinHighlights") ?: false,
            fields.text("density") ?: TaskViewConfig.DENSITY_NORMAL,
            fields.array("bins")?.map { enumIn(it, "bins", TaskBin.entries) { bin -> bin.wire } },
            fields.int("seed"),
            fields.extra(NOTHING_COMPUTED),
        )
    }

    private fun readFilter(json: JsonElement): TaskViewFilter {
        val fields = Fields(obj(json, "only"))
        return TaskViewFilter(fields.texts("due"), fields.texts("time"), fields.texts("old"), fields.bool("highlightedOnly") ?: false, fields.extra(NOTHING_COMPUTED))
    }

    fun readSyncLogItem(json: JsonElement): SyncTaskLogItem {
        val fields = Fields(obj(json, "history entry"))
        val entry = readEntry(fields.element("entry") ?: throw missing("entry"))
        return SyncTaskLogItem(entry, fields.extra(ITEM_COMPUTED))
    }

    fun readEntry(json: JsonElement): TaskHistoryEntry {
        val fields = Fields(obj(json, "entry"))
        return TaskHistoryEntry(
            fields.guid("id") ?: EMPTY_ID, fields.enumOf("kind", TaskHistoryKind.entries) { it.wire } ?: TaskHistoryKind.FINISHED,
            fields.time("when") ?: DotNetTime.MIN, readTask(fields.element("task") ?: throw missing("snapshot")), fields.time("editedAt") ?: DotNetTime.MIN,
            fields.time("deletedAt"), fields.extra(LIVE_COMPUTED),
        )
    }

    /** The account's settings as written (not cleaned; [readAccountSettings] cleans). */
    fun readAccountSettingsObject(json: JsonElement): SyncTaskAccountSettings {
        val fields = Fields(obj(json, "taskAccountSettings"))
        return SyncTaskAccountSettings(
            fields.guid("defaultViewId"), fields.element("defaultViewStamp")?.let(::readStamp), fields.text("pickedCalendarId"),
            fields.element("pickedCalendarStamp")?.let(::readStamp), fields.guids("ringOn"), fields.element("ringOnStamp")?.let(::readStamp),
            fields.extra(ACCOUNT_COMPUTED),
        )
    }

    // ---------------------------------------------------------------- helpers

    private fun texts(values: List<String>): JsonArray = JsonArray(values.map { JsonPrimitive(it) })

    // Unknown fields go back after the known ones.
    private fun JsonObjectBuilder.putExtra(extra: JsonObject, unknown: Boolean) {
        if (unknown) {
            extra.forEach { (key, value) -> put(key, value) }
        }
    }

    private fun missing(name: String) = IllegalArgumentException("Task JSON: \"$name\" is missing or of the wrong kind")

    private fun obj(json: JsonElement, name: String): JsonObject = json as? JsonObject ?: throw missing(name)

    private fun textOf(json: JsonElement, name: String): String = (json as? JsonPrimitive)?.takeIf { it.isString }?.content ?: throw missing(name)

    // A number, or a number in a string (JsonNumberHandling.AllowReadingFromString).
    private fun intOf(json: JsonElement, name: String): Int {
        val primitive = json as? JsonPrimitive ?: throw missing(name)
        return primitive.content.trim().toIntOrNull() ?: throw missing(name)
    }

    private fun guidOf(json: JsonElement, name: String): String {
        val text = textOf(json, name)
        return if (guidForm.matches(text)) text.lowercase() else throw missing(name)
    }

    // By its exact name, or its number (also a number written as text), as the PC reads an enum; another name or number is a newer
    // app's value. The PC takes no other spelling of a name ("Sort" is not "sort").
    private fun <T> enumIn(json: JsonElement, name: String, values: List<T>, wire: (T) -> String): T {
        val primitive = json as? JsonPrimitive ?: throw missing(name)
        if (primitive.isString) {
            values.firstOrNull { wire(it) == primitive.content }?.let { return it }
        } else if (primitive.content == "true" || primitive.content == "false") {
            throw missing(name)
        }

        return primitive.content.toIntOrNull()?.let { values.getOrNull(it) }
            ?: throw TaskNewerValueException("Task JSON: \"$name\" holds a value this version doesn't know")
    }

    /** One JSON object's fields by the PC's names (any case, an exact match first), remembering which were asked for. */
    private class Fields(private val json: JsonObject) {
        private val known = HashSet<String>()

        /** The field's value; null when it is missing or null. */
        fun element(name: String): JsonElement? {
            known += name.lowercase()
            val value = json[name] ?: json.entries.firstOrNull { it.key.equals(name, ignoreCase = true) }?.value
            return value.takeIf { it !is JsonNull }
        }

        fun text(name: String): String? = element(name)?.let { textOf(it, name) }

        fun int(name: String): Int? = element(name)?.let { intOf(it, name) }

        fun double(name: String): Double? = element(name)?.let { element ->
            val primitive = element as? JsonPrimitive ?: throw missing(name)
            primitive.content.trim().toDoubleOrNull()?.takeIf { !it.isNaN() && !it.isInfinite() } ?: throw missing(name)
        }

        fun bool(name: String): Boolean? = element(name)?.let { element ->
            val primitive = element as? JsonPrimitive
            if (primitive == null || primitive.isString) throw missing(name)
            primitive.content.toBooleanStrictOrNull() ?: throw missing(name)
        }

        fun guid(name: String): String? = element(name)?.let { guidOf(it, name) }

        fun array(name: String): JsonArray? = element(name)?.let { it as? JsonArray ?: throw missing(name) }

        fun texts(name: String): List<String>? = array(name)?.map { textOf(it, name) }

        fun guids(name: String): List<String>? = array(name)?.map { guidOf(it, name) }

        fun time(name: String): OffsetDateTime? = text(name)?.let {
            try {
                DotNetTime.cut(OffsetDateTime.parse(GoalJson.unmark(it)))
            } catch (e: DateTimeParseException) {
                throw IllegalArgumentException("Not a time: $name", e)
            }
        }

        fun date(name: String): LocalDate? = text(name)?.let {
            try {
                LocalDate.parse(it)
            } catch (e: DateTimeParseException) {
                throw IllegalArgumentException("Not a date: $name", e)
            }
        }

        fun timeOfDay(name: String): LocalTime? = text(name)?.let {
            try {
                DotNetTime.cut(LocalTime.parse(it))
            } catch (e: DateTimeParseException) {
                throw IllegalArgumentException("Not a time of day: $name", e)
            }
        }

        fun <T> enumOf(name: String, values: List<T>, wire: (T) -> String): T? = element(name)?.let { enumIn(it, name, values, wire) }

        /** The fields nobody asked for, in their order, without the PC's computed names. */
        fun extra(computed: Array<String>): JsonObject {
            val unknown = json.filterKeys { key -> key.lowercase() !in known && computed.none { it.equals(key, ignoreCase = true) } }
            return if (unknown.isEmpty()) TaskUnknown.NONE else JsonObject(unknown)
        }
    }
}
