package com.accountabilibuddy.companion.debug

import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.shared.sync.SessionTaskItem
import com.accountabilibuddy.shared.sync.SyncLaterItem
import com.accountabilibuddy.shared.sync.SyncParked
import java.time.Duration
import java.time.Instant
import java.time.LocalDate
import java.util.UUID

/**
 * Debug builds only (Part 3, Tasks). Made-up lists for the Tasks screenshots, a read of the phone's own outgoing sync file that
 * prints ids, times and flags only (never a title, step or note), and a run of the Later merge rules against the real store.
 */
class TasksDemo(private val app: CompanionApp) {
    private val lists get() = app.sharedLists

    private val work = "Work" to "#2563EB"
    private val home = "Home" to "#16A34A"
    private val errands = "Errands" to "#D97706"

    /** Tombstones every live item of the three lists (the seeder's own and any earlier extras). */
    fun clearLists() {
        lists.nextTasks.value.forEach { lists.deleteNextTask(it.id) }
        lists.parked.value.forEach { lists.deleteParked(it.id) }
        lists.later.value.forEach { lists.deleteLater(it.id) }
    }

    /** Replaces the lists with the ones board 5v shows: 3 session tasks, 2 come-backs, 11 Later items, 4 done today. [after] is the seeder's line. */
    fun seed(after: String): String {
        clearLists()
        val now = Instant.now()
        fun next(title: String, cat: Pair<String, String>, estimate: Int?, steps: List<Pair<String, Boolean>> = emptyList(), note: String? = null): String {
            val id = lists.addNextTask(title, cat.first, cat.second)!!
            lists.updateNextTask(id) { task ->
                task.copy(
                    estimateMinutes = estimate, note = note,
                    subtasks = steps.map { (text, done) -> SessionTaskItem(UUID.randomUUID().toString(), text, if (done) now else null, emptyList()) }.ifEmpty { null },
                )
            }
            return id
        }
        next("Draft Q4 report outline", work, 50, listOf("Collect last quarter's numbers" to true, "Sketch the sections" to false, "Write the summary" to false), "Keep it to one page. Ask Dana for the totals.")
        next("Reply to Dana about the invoices", work, 25)
        next("Review Sam's PR #482", errands, null)
        val done = next("Send the weekly update", work, null)
        lists.toggleNextTask(done)

        fun park(text: String, cat: Pair<String, String>?, hoursAgo: Long) {
            lists.receive(
                null,
                listOf(SyncParked(UUID.randomUUID().toString(), text, now.minus(Duration.ofHours(hoursAgo)), null, now, null, hoursAgo.toDouble(), cat?.first, cat?.second)),
                null,
            )
        }
        park("Book the dentist", errands, 3)
        park("Ask Sam about the API key", work, 5)

        val today = LocalDate.now()
        fun later(title: String, cat: Pair<String, String>?, dueIn: Long?, estimate: Int? = null, steps: Int = 0, note: String? = null) {
            val id = lists.addLater(title, cat?.first, cat?.second)!!
            lists.updateLater(id) { item ->
                item.copy(
                    dueOn = dueIn?.let { today.plusDays(it).toString() }, estimateMinutes = estimate, note = note,
                    subtasks = (1..steps).map { SessionTaskItem(UUID.randomUUID().toString(), "Step $it of $title", null, emptyList()) }.ifEmpty { null },
                )
            }
        }
        later("Expense report", work, 4, 50, 2)
        later("Renew the car insurance", errands, 9)
        later("Fix the leaking tap", home, null, 90, 0, "Washers are in the garage drawer.")
        later("Plan the team offsite", work, null, 90, 3)
        later("Return the library books", errands, 12)
        later("Call the landlord", home, null)
        later("Clean up the shared drive", work, null, 50)
        later("Water the plants", home, null, 25)
        later("Order printer toner", errands, null)
        later("Read the onboarding guide", null, null)
        later("Back up the photo library", home, null, 90)
        // One ticked off on this phone (Done today).
        lists.later.value.firstOrNull { it.title == "Water the plants" }?.let { lists.tickLater(it.id) }
        later("Archive the 2024 contracts", work, null)
        return "$after; tasks lists: ${lists.nextTasks.value.size} next (1 done), ${lists.parked.value.size} come-backs, ${lists.later.value.size} later"
    }

    /** What this phone's own sync file carries for the three lists: ids (first 8), edit time, deleted flag and counts. No text. */
    fun dump(): String {
        val file = app.sync.ownFile()
        fun stamp(time: Instant) = time.toString()
        val builder = StringBuilder("outgoing file lists:")
        builder.append("\n nextTasks=${file.nextTasks?.size}")
        file.nextTasks?.forEach { builder.append("\n  ${it.id.take(8)} edited=${stamp(it.editedAt)} deleted=${it.deletedAt != null} done=${it.isDone} pos=${it.position}") }
        builder.append("\n parked=${file.parked?.size}")
        file.parked?.forEach { builder.append("\n  ${it.id.take(8)} edited=${stamp(it.editedAt)} deleted=${it.deletedAt != null} done=${it.doneAt != null} cat=${it.category != null}") }
        builder.append("\n later=${file.later?.size}")
        file.later?.forEach {
            builder.append("\n  ${it.id.take(8)} edited=${stamp(it.editedAt)} deleted=${it.deletedAt != null} due=${it.dueOn != null} est=${it.estimateMinutes} steps=${it.subtasks?.size} note=${it.note != null} titleEmpty=${it.title.isEmpty()}")
        }
        return builder.toString()
    }

    /**
     * Runs the real store against another device's versions of one Later item (ids, times and flags only are printed):
     * an older PC edit must lose, a newer one must win, an item only the PC has must be kept and passed on, and a newer
     * deletion must remove the item here and leave a tombstone with no text in the outgoing list.
     */
    fun mergeCheck(): String {
        val id = lists.addLater("merge check one") ?: return "could not add"
        lists.updateLater(id) { it.copy(title = "merge check phone edit", estimateMinutes = 50) }
        val mine = lists.laterForSync().first { it.id == id }
        val other = UUID.randomUUID().toString()
        fun pcCopy(title: String, at: Instant, deleted: Instant? = null) = SyncLaterItem(
            id, title, null, null, null, 25, null, null, at, deleted, mine.position,
        )

        // 1. A PC edit older than the phone's: the phone's stays.
        lists.receive(null, null, listOf(pcCopy("pc older", mine.editedAt.minusSeconds(5))))
        val olderLost = lists.later.value.first { it.id == id }.let { it.title == "merge check phone edit" && it.estimateMinutes == 50 }
        // 2. A PC edit newer than the phone's, plus an item only the PC has: the PC's wins, the other is kept.
        val onlyPc = SyncLaterItem(other, "merge check pc only", null, null, null, null, null, null, mine.editedAt, null, mine.position + 1)
        lists.receive(null, null, listOf(pcCopy("pc newer", mine.editedAt.plusSeconds(5)), onlyPc))
        val newerWon = lists.later.value.first { it.id == id }.title == "pc newer"
        val onlyPcKept = lists.later.value.any { it.id == other } && lists.laterForSync().any { it.id == other && it.title.isNotEmpty() }
        // 3. The phone edits again (it must outrank the PC's newer edit, one tick past it at least).
        lists.updateLater(id) { it.copy(estimateMinutes = 90) }
        val phoneAfter = lists.laterForSync().first { it.id == id }
        val phoneOutranks = phoneAfter.editedAt.isAfter(mine.editedAt.plusSeconds(5))
        // 4. A deletion newer than the phone's edit: the item goes here and a tombstone with no text is passed on.
        lists.receive(null, null, listOf(pcCopy("", phoneAfter.editedAt.plusSeconds(5), phoneAfter.editedAt.plusSeconds(5))))
        val gone = lists.later.value.none { it.id == id }
        val tombstone = lists.laterForSync().firstOrNull { it.id == id }?.let { it.deletedAt != null && it.title.isEmpty() && it.note == null } == true
        lists.deleteLater(other)
        return "merge-check: olderLost=$olderLost newerWon=$newerWon onlyPcKept=$onlyPcKept phoneOutranks=$phoneOutranks deletionWon=$gone tombstonePassedOnNoText=$tombstone"
    }
}
