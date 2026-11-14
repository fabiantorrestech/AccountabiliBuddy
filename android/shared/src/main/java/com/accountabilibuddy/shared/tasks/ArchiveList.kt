package com.accountabilibuddy.shared.tasks

import java.time.Duration
import java.time.Instant
import java.time.ZoneId
import java.time.format.DateTimeFormatter
import java.util.Locale

/** What an Archive line is (the PC Notebook's kinds). */
enum class ArchiveKind(val label: String) {
    COMEBACK("Come-back"),
    NOTE("Note"),
    UNFINISHED("Unfinished task"),
    DONE("Done task"),
    UNFINISHED_STEP("Unfinished subtask"),
    DONE_STEP("Done subtask"),
}

/** The Archive's filter choices, in the PC's order; each shows one kind (steps go with their task's kind) or everything. */
enum class ArchiveFilter(val label: String) {
    EVERYTHING("Everything"),
    COMEBACKS("Come-backs"),
    NOTES("Notes"),
    UNFINISHED("Unfinished tasks"),
    DONE("Done tasks"),
    ;

    fun matches(kind: ArchiveKind): Boolean = when (this) {
        EVERYTHING -> true
        COMEBACKS -> kind == ArchiveKind.COMEBACK
        NOTES -> kind == ArchiveKind.NOTE
        UNFINISHED -> kind == ArchiveKind.UNFINISHED || kind == ArchiveKind.UNFINISHED_STEP
        DONE -> kind == ArchiveKind.DONE || kind == ArchiveKind.DONE_STEP
    }
}

/** A come-back as the Archive lists it (open and ticked ones). The text is the user's: shown, never logged. */
data class ArchiveComeback(val id: String, val text: String, val createdAt: Instant, val doneAt: Instant?)

/**
 * One line of the Archive list, as the PC's Notebook builds it. [id] is the task's, step's, note's or come-back's id; [parentId] is a step's task.
 * [text] is the user's private text: shown, never logged.
 *
 * @property canEdit The text can be changed here (a note, a come-back; never a received or kept task or its steps).
 * @property editHint Why it can't ("Edit it on Work PC"), null when it can or there is nothing to say.
 * @property editWarning A note's edit says it reaches every device (only while "Sync task lists" is on).
 * @property deleteWarning A note's delete says the same.
 */
data class ArchiveItem(
    val kind: ArchiveKind,
    val id: String,
    val parentId: String?,
    val text: String,
    val whenText: String,
    val sort: Instant,
    val isTicked: Boolean,
    val canTick: Boolean,
    val canEdit: Boolean,
    val canDelete: Boolean,
    val editHint: String? = null,
    val editWarning: String? = null,
    val deleteWarning: String? = null,
) {
    val isStep: Boolean get() = kind == ArchiveKind.UNFINISHED_STEP || kind == ArchiveKind.DONE_STEP
}

/** A line of Recently deleted; [row] says how Restore works (a History entry, or a settle record stamped over the delete). */
data class ArchiveDeletedLine(val row: ArchiveDeletedRow, val text: String)

/**
 * The Archive page's list, pure (AR1c): the phone's twin of the PC `NotebookViewModel`'s assembly: come-backs, notes and the builder's tasks
 * (each followed by its steps) newest first, the filter and the search over their text, the summary line and Recently deleted. Same input,
 * same lines in the same order as the PC. Times are 12-hour with AM/PM.
 */
object ArchiveList {
    const val NOTE_EDIT_WARNING = "Saving changes it on all your devices"
    const val NOTE_DELETE_WARNING = "Deletes it on all your devices"

    /** "Tue 11/12 4:05 PM" in [zone]: the day name in the phone's language, the time always 12-hour with AM/PM (never the system's 24-hour setting). */
    fun whenText(time: Instant, zone: ZoneId, locale: Locale = Locale.getDefault()): String {
        val local = time.atZone(zone)
        val day = DateTimeFormatter.ofPattern("EEE", locale).format(local)
        val rest = DateTimeFormatter.ofPattern("M/d h:mm a", Locale.US).format(local)
        return "$day $rest"
    }

    /**
     * Every line, unfiltered, newest first. [categoryName] gives a task's label name as it is now (id, name it was stored with); [notesTravel] is "Sync task
     * lists" being on, which adds the two "on all your devices" lines to a note.
     */
    fun items(
        archive: ArchiveRows,
        comebacks: List<ArchiveComeback>,
        notesTravel: Boolean,
        categoryName: (String?, String?) -> String?,
        whenText: (Instant) -> String,
    ): List<ArchiveItem> {
        val all = ArrayList<ArchiveItem>()
        for (comeback in comebacks) {
            val text = comeback.doneAt?.let { "Parked ${whenText(comeback.createdAt)} · ticked ${whenText(it)}" } ?: "Parked ${whenText(comeback.createdAt)} · open"
            all.add(ArchiveItem(ArchiveKind.COMEBACK, comeback.id, null, comeback.text, text, comeback.createdAt, comeback.doneAt != null, canTick = true, canEdit = true, canDelete = true))
        }

        for (note in archive.notes) {
            val from = note.fromText?.let { " · $it" } ?: ""
            all.add(
                ArchiveItem(
                    ArchiveKind.NOTE, note.id, null, note.text, "Noted ${whenText(note.at)}$from", note.at, isTicked = false, canTick = false, canEdit = true, canDelete = true,
                    editWarning = if (notesTravel) NOTE_EDIT_WARNING else null, deleteWarning = if (notesTravel) NOTE_DELETE_WARNING else null,
                ),
            )
        }

        for (row in archive.tasks) {
            val category = categoryName(row.categoryId, row.category)?.let { "$it · " } ?: ""
            val where = when {
                row.source == ArchiveSource.TASK_LIST -> "From your task list"
                row.outside -> "Done outside a session"
                else -> "Session of ${whenText(row.sessionStart ?: row.sort)}"
            }
            val on = when (row.source) {
                ArchiveSource.RECEIVED -> " · on ${row.deviceName}"
                ArchiveSource.KEPT -> " · kept here from ${row.deviceName}"
                else -> ""
            }
            val state = row.doneAt?.let { "done ${whenText(it)}" } ?: "left open"
            val hint = if (row.canEdit) null else row.deviceName?.let { "Edit it on $it" } ?: "Edit it on the Tasks page"
            all.add(
                ArchiveItem(
                    if (row.isDone) ArchiveKind.DONE else ArchiveKind.UNFINISHED, row.id, null, row.title, "$category$where · $state$on", row.sort,
                    row.isDone, row.canTick, row.canEdit, row.canDelete, hint,
                ),
            )
            var order = 0L
            for (step in row.steps) {
                // Just after its task in the newest-first list (the PC: sort minus one 100 ns tick per step).
                order++
                val stepDone = step.doneAt != null
                all.add(
                    ArchiveItem(
                        if (stepDone) ArchiveKind.DONE_STEP else ArchiveKind.UNFINISHED_STEP, step.id, row.id, step.title,
                        step.doneAt?.let { "Done ${whenText(it)}" } ?: "Left open", row.sort.minus(Duration.ofNanos(order * 100)),
                        stepDone, canTick = row.canEdit, canEdit = row.canEdit, canDelete = row.canEdit, editHint = hint,
                    ),
                )
            }
        }

        return all.sortedByDescending { it.sort }
    }

    /** What the filter and the search leave of [items] (the search is the trimmed text, any case, anywhere in the line's text). */
    fun shown(items: List<ArchiveItem>, filter: ArchiveFilter, search: String): List<ArchiveItem> {
        val needle = search.trim()
        return items.filter { filter.matches(it.kind) && (needle.isEmpty() || it.text.contains(needle, ignoreCase = true)) }
    }

    /** E.g. "3 open come-backs · 12 notes · 4 unfinished · 31 done". */
    fun summary(items: List<ArchiveItem>): String {
        val open = items.count { it.kind == ArchiveKind.COMEBACK && !it.isTicked }
        return "$open open come-backs · ${items.count { it.kind == ArchiveKind.NOTE }} notes · ${items.count { it.kind == ArchiveKind.UNFINISHED }} unfinished · ${items.count { it.kind == ArchiveKind.DONE }} done"
    }

    /** Recently deleted: the builder's synced lines the search matches, newest first, "Deleted Tue 11/12 4:05 PM · on Work PC". */
    fun deleted(archive: ArchiveRows, search: String, whenText: (Instant) -> String): List<ArchiveDeletedLine> {
        val needle = search.trim()
        return archive.deleted
            .filter { needle.isEmpty() || it.title.contains(needle, ignoreCase = true) }
            .map { row -> ArchiveDeletedLine(row, "Deleted ${whenText(row.deletedAt)}${row.deviceName?.let { " · on $it" } ?: ""}") }
            .sortedByDescending { it.row.deletedAt }
    }

    /** "Recently deleted · 3". */
    fun deletedHeader(count: Int): String = "Recently deleted · $count"

    /** The group shows open while a search is on, so a hit can't hide in a folded one. */
    fun showsDeleted(open: Boolean, search: String): Boolean = open || search.trim().isNotEmpty()
}

/**
 * The last known name of each other device, by lower-case id, for "from <device>" on a note (AR1b, the PC's `NotebookDeviceNames`). Sources, the first
 * that knows an id wins: the devices of the group now, the names the received task histories carry, then the removal list (a removed device's notes
 * stay on the others, and its name is remembered there). An id no source knows reads "another device".
 */
object ArchiveDeviceNames {
    fun of(current: List<Pair<String, String>>, fromHistory: List<Pair<String, String>>, removed: List<Pair<String, String>>): Map<String, String> {
        val names = LinkedHashMap<String, String>()
        for ((id, name) in current + fromHistory + removed) {
            if (name.isNotBlank()) names.putIfAbsent(id.lowercase(), name)
        }
        return names
    }
}
