package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.sync.PastSettleEdits
import com.accountabilibuddy.shared.sync.SessionNoteEdits
import com.accountabilibuddy.shared.sync.SessionNoteRules
import com.accountabilibuddy.shared.sync.SessionNotesBackup
import com.accountabilibuddy.shared.sync.SessionTaskItem
import com.accountabilibuddy.shared.sync.SyncHistoryTask
import com.accountabilibuddy.shared.sync.SyncPastSettled
import com.accountabilibuddy.shared.sync.SyncSessionNote
import com.accountabilibuddy.shared.sync.SyncTaskHistoryEntry
import com.accountabilibuddy.shared.tasks.ArchiveBuilder
import com.accountabilibuddy.shared.tasks.ArchiveComeback
import com.accountabilibuddy.shared.tasks.ArchiveDeviceNames
import com.accountabilibuddy.shared.tasks.ArchiveFilter
import com.accountabilibuddy.shared.tasks.ArchiveInput
import com.accountabilibuddy.shared.tasks.ArchiveKind
import com.accountabilibuddy.shared.tasks.ArchiveList
import com.accountabilibuddy.shared.tasks.ArchiveReceivedDevice
import com.accountabilibuddy.shared.tasks.ArchiveRows
import com.accountabilibuddy.shared.tasks.TaskLists
import java.io.File
import java.time.Duration
import java.time.Instant
import java.time.ZoneOffset
import java.util.Locale

/**
 * AR1c (the phone's full Archive, ADR 87): the pure rules the page is built from, run on one made-up world (a Work PC's history, come-backs and the
 * notes of three devices), and scans of the Android wiring (this module has no Context). The rows themselves are checked against the PC's Core builder by
 * the `ar1` fixtures; here: the list's order, filters, search and 12-hour texts, tick / untick / delete / Restore through settle records, a note's edit and
 * delete reaching the sync file, and where note text never goes (the watch, a plain backup, a log).
 */
object Ar1cSelfCheck {
    private val now: Instant = Instant.parse("2026-11-13T12:00:00Z")
    private const val SELF = "55555555-0000-0000-0000-000000000005"
    private const val WORK = "aaaaaaaa-0000-0000-0000-00000000000a"
    private const val GONE = "99999999-0000-0000-0000-000000000009"
    private fun id(n: Int) = "a1a1a1a1-0000-0000-0000-%012d".format(n)
    private fun at(text: String) = Instant.parse(text)

    private val t1 = SyncHistoryTask(
        id(1), "Email Sam", at("2026-11-12T17:00:00Z"),
        listOf(SessionTaskItem(id(11), "Draft it", at("2026-11-12T16:30:00Z"), emptyList()), SessionTaskItem(id(12), "Send it", null, emptyList())),
    )
    private val t2 = SyncHistoryTask(id(2), "Call Ana", null, emptyList())
    private val history = listOf(SyncTaskHistoryEntry(at("2026-11-12T16:05:00Z"), listOf(t1, t2)))

    private fun note(n: Int, text: String, whenAt: String, by: String, edited: String = whenAt) =
        SyncSessionNote(id(n), text, at(whenAt), null, null, by, at(edited))

    private val notes = listOf(
        note(21, "typed on the work PC", "2026-11-12T09:00:00Z", WORK),
        note(22, "typed here", "2026-11-12T10:30:00Z", SELF),
        note(23, "typed on a removed device", "2026-11-12T08:00:00Z", GONE),
        SyncSessionNote(id(24), null, at("2026-11-12T12:00:00Z"), null, null, WORK, at("2026-11-12T12:30:00Z"), at("2026-11-12T12:30:00Z")),
    )
    private val comebacks = listOf(
        ArchiveComeback(id(31), "Buy milk", at("2026-11-12T10:00:00Z"), null),
        ArchiveComeback(id(32), "Old idea", at("2026-11-10T09:00:00Z"), at("2026-11-11T08:00:00Z")),
    )

    private fun rows(settled: List<SyncPastSettled> = emptyList(), noteList: List<SyncSessionNote> = notes, names: Map<String, String> = mapOf(WORK to "Work PC")): ArchiveRows =
        ArchiveBuilder.build(
            ArchiveInput(emptyList(), listOf(ArchiveReceivedDevice(WORK, "Work PC", history)), emptyList(), settled, TaskLists.EMPTY, now, emptySet(), noteList, SELF, names),
        )

    private fun whenText(time: Instant) = ArchiveList.whenText(time, ZoneOffset.UTC, Locale.US)

    private fun items(archive: ArchiveRows = rows(), travel: Boolean = true) = ArchiveList.items(archive, comebacks, travel, { _, stored -> stored }, ::whenText)

    private fun ids(list: List<com.accountabilibuddy.shared.tasks.ArchiveItem>) = list.map { it.id }

    fun run(report: CheckReport) {
        listAndFilters(report)
        texts(report)
        settleRecords(report)
        noteRules(report)
        sourceChecks(report)
    }

    private fun listAndFilters(report: CheckReport) {
        val all = items()
        // Newest first; a task's steps follow it (one 100 ns tick apart); a note by a removed device and the come-backs sit among the rest by time.
        val expected = listOf(id(1), id(11), id(12), id(2), id(22), id(31), id(21), id(23), id(32))
        report.check("ar1c: the list is the PC's order: every kind newest first, a task's steps right after it", ids(all) == expected, "${ids(all)}")
        val tie = Instant.parse("2026-11-12T08:00:00Z")
        val tied = ArchiveList.items(
            ArchiveRows(
                rows().tasks.filter { it.id == id(2) }.map { it.copy(doneAt = null, sort = tie) }, emptyList(),
                listOf(com.accountabilibuddy.shared.tasks.ArchiveNoteRow(id(40), "tied note", tie, SELF, null, false)),
            ),
            listOf(ArchiveComeback(id(41), "tied come-back", tie, null)), true, { _, s -> s }, ::whenText,
        )
        report.check("ar1c: at the same time the PC lists come-backs, then notes, then tasks", ids(tied) == listOf(id(41), id(40), id(2)), "${ids(tied)}")
        report.check("ar1c: the filter Come-backs shows only them", ids(ArchiveList.shown(all, ArchiveFilter.COMEBACKS, "")) == listOf(id(31), id(32)))
        report.check("ar1c: the filter Notes shows every device's live notes, newest first", ids(ArchiveList.shown(all, ArchiveFilter.NOTES, "")) == listOf(id(22), id(21), id(23)))
        report.check("ar1c: the filter Unfinished tasks shows the open task and the open step", ids(ArchiveList.shown(all, ArchiveFilter.UNFINISHED, "")) == listOf(id(12), id(2)))
        report.check("ar1c: the filter Done tasks shows the done task and its done step", ids(ArchiveList.shown(all, ArchiveFilter.DONE, "")) == listOf(id(1), id(11)))
        report.check("ar1c: the filter Everything shows all of it", ArchiveList.shown(all, ArchiveFilter.EVERYTHING, "").size == all.size)
        report.check("ar1c: the filters are the PC's five, in its order", ArchiveFilter.entries.map { it.label } == listOf("Everything", "Come-backs", "Notes", "Unfinished tasks", "Done tasks"))
        report.check("ar1c: search finds a task in any case, trimmed, and keeps the order", ids(ArchiveList.shown(all, ArchiveFilter.EVERYTHING, "  EMAIL ")) == listOf(id(1)))
        report.check("ar1c: search reaches notes (every device's) and steps", ids(ArchiveList.shown(all, ArchiveFilter.EVERYTHING, "typed")) == listOf(id(22), id(21), id(23)) && ids(ArchiveList.shown(all, ArchiveFilter.EVERYTHING, "send it")) == listOf(id(12)))
        report.check("ar1c: search and a filter together narrow it", ids(ArchiveList.shown(all, ArchiveFilter.COMEBACKS, "milk")) == listOf(id(31)) && ArchiveList.shown(all, ArchiveFilter.NOTES, "milk").isEmpty())
        report.check("ar1c: the summary counts open come-backs, notes, unfinished and done tasks (steps not counted)", ArchiveList.summary(all) == "1 open come-backs · 3 notes · 1 unfinished · 1 done", ArchiveList.summary(all))
        report.check("ar1c: a deleted note is not in the list", ids(all).none { it == id(24) })
        report.check("ar1c: the phone's own history is empty, so every task row is another PC's", rows().tasks.all { it.source == com.accountabilibuddy.shared.tasks.ArchiveSource.RECEIVED })
    }

    private fun texts(report: CheckReport) {
        val all = items()
        fun of(n: Int) = all.first { it.id == id(n) }
        report.check("ar1c: times are 12-hour with AM and PM, whatever the system's setting", whenText(at("2026-11-12T16:05:00Z")) == "Thu 11/12 4:05 PM" && whenText(at("2026-11-12T00:30:00Z")) == "Thu 11/12 12:30 AM" && whenText(at("2026-11-12T12:00:00Z")) == "Thu 11/12 12:00 PM", whenText(at("2026-11-12T16:05:00Z")))
        report.check("ar1c: no line carries a 24-hour time", all.none { Regex("""\b(1[3-9]|2[0-3]):\d\d""").containsMatchIn(it.whenText) }, all.map { it.whenText }.toString())
        report.check("ar1c: a task says its session, when it was done and the PC", of(1).whenText == "Session of Thu 11/12 4:05 PM · done Thu 11/12 5:00 PM · on Work PC", of(1).whenText)
        report.check("ar1c: an open task says it was left open", of(2).whenText == "Session of Thu 11/12 4:05 PM · left open · on Work PC", of(2).whenText)
        report.check("ar1c: a come-back says parked and ticked, or open", of(31).whenText == "Parked Thu 11/12 10:00 AM · open" && of(32).whenText == "Parked Tue 11/10 9:00 AM · ticked Wed 11/11 8:00 AM", "${of(31).whenText} / ${of(32).whenText}")
        report.check("ar1c: this device's note is unmarked", of(22).whenText == "Noted Thu 11/12 10:30 AM", of(22).whenText)
        report.check("ar1c: another device's note says from its name", of(21).whenText == "Noted Thu 11/12 9:00 AM · from Work PC", of(21).whenText)
        report.check("ar1c: a removed device's note says from another device, and stays", of(23).whenText == "Noted Thu 11/12 8:00 AM · from another device", of(23).whenText)
        val removedNamed = rows(names = mapOf(WORK to "Work PC", GONE to "Old laptop"))
        report.check("ar1c: a removed device's remembered name is used", items(removedNamed).first { it.id == id(23) }.whenText.endsWith("from Old laptop"))
        report.check("ar1c: device names: the group first, then the histories, then the removal list; a blank name is skipped",
            ArchiveDeviceNames.of(listOf(WORK.uppercase() to "Work PC", GONE to " "), listOf(WORK to "Old name"), listOf(GONE to "Old laptop")) == mapOf(WORK to "Work PC", GONE to "Old laptop"))
        report.check("ar1c: another PC's task cannot be renamed here and says where to edit it", !of(1).canEdit && of(1).editHint == "Edit it on Work PC" && !of(11).canTick && !of(11).canEdit)
        report.check("ar1c: a task can be ticked and deleted from here", of(2).canTick && of(2).canDelete && of(1).canTick)
        report.check("ar1c: a note says its edit and delete reach every device while Sync task lists is on",
            of(21).editWarning == "Saving changes it on all your devices" && of(21).deleteWarning == "Deletes it on all your devices")
        val alone = items(travel = false).first { it.id == id(21) }
        report.check("ar1c: with Sync task lists off a note says nothing about other devices", alone.editWarning == null && alone.deleteWarning == null)
        report.check("ar1c: a note and a come-back can be edited and deleted here", of(21).canEdit && of(21).canDelete && of(31).canEdit && of(31).canDelete)
    }

    private fun settleRecords(report: CheckReport) {
        // Tick, untick and delete of a past task are settle records (the owner's history is never touched: the phone has none of its own).
        val ticked = PastSettleEdits.settle(emptyList(), id(2), SyncPastSettled.DONE, at("2026-11-13T10:00:00Z"))
        val afterTick = rows(ticked).tasks.first { it.id == id(2) }
        report.check("ar1c: ticking another PC's open task is one settle record, and it shows done at once", ticked.size == 1 && ticked[0].state == SyncPastSettled.DONE && afterTick.isDone && afterTick.doneAt == ticked[0].editedAt)
        val unticked = PastSettleEdits.settle(emptyList(), id(1), SyncPastSettled.OPEN, at("2026-11-13T10:00:00Z"))
        report.check("ar1c: unticking a done task of another PC shows it open at once, steps as they were", rows(unticked).tasks.first { it.id == id(1) }.let { !it.isDone && it.steps.size == 2 && it.steps[0].doneAt != null })
        val deleted = PastSettleEdits.settle(emptyList(), id(1), SyncPastSettled.DELETED, at("2026-11-13T10:00:00Z"))
        val afterDelete = rows(deleted)
        report.check("ar1c: deleting a past task is a settle record: it leaves the list and is in Recently deleted", afterDelete.tasks.none { it.id == id(1) } && afterDelete.deleted.map { it.id } == listOf(id(1)))
        val line = ArchiveList.deleted(afterDelete, "", ::whenText).single()
        report.check("ar1c: the Recently deleted line says when and on which PC, and offers Restore as done", line.text == "Deleted Fri 11/13 10:00 AM · on Work PC" && line.row.canRestoreByStamp && line.row.restoreAs == SyncPastSettled.DONE && !line.row.canRestore, line.text)
        report.check("ar1c: Recently deleted has the PC's header, folds unless searching, and a search narrows it",
            ArchiveList.deletedHeader(1) == "Recently deleted · 1" && !ArchiveList.showsDeleted(false, "  ") && ArchiveList.showsDeleted(false, "x") && ArchiveList.showsDeleted(true, "") &&
                ArchiveList.deleted(afterDelete, "email", ::whenText).size == 1 && ArchiveList.deleted(afterDelete, "nothing", ::whenText).isEmpty())

        // Restore: a record of the state it had, stamped newer than the delete: never a raw restore, never a removed record.
        val restored = PastSettleEdits.restore(deleted, id(1), line.row.restoreAs!!, at("2026-11-13T10:00:00Z"))
        report.check("ar1c: Restore stamps a settle record newer than the delete (the clock reads the same instant)", restored.size == 1 && restored[0].editedAt.isAfter(deleted[0].editedAt) && restored[0].state == SyncPastSettled.DONE, "$restored")
        val back = rows(restored)
        report.check("ar1c: after Restore the task is a task again and the line is gone", back.tasks.any { it.id == id(1) } && back.deleted.isEmpty())
        report.check("ar1c: Restore writes only Done or Open, and an unknown state changes nothing", PastSettleEdits.restore(deleted, id(1), SyncPastSettled.DELETED, now) == deleted && PastSettleEdits.restore(deleted, id(1), "moved", now) == deleted)
        report.check("ar1c: Restore keeps every other record", PastSettleEdits.restore(listOf(deleted[0], SyncPastSettled(id(2), SyncPastSettled.DONE, at("2026-11-13T09:00:00Z"))), id(1), SyncPastSettled.OPEN, now).size == 2)
        val olderClock = PastSettleEdits.restore(deleted, id(1), SyncPastSettled.OPEN, at("2026-11-13T09:00:00Z"))
        report.check("ar1c: even with a clock behind the delete's, Restore is stamped after it", olderClock[0].editedAt.isAfter(deleted[0].editedAt))
        // The 90 days of another PC's history, cut on receipt: no time, 91 days and more go; 90 days exactly (the PC's >=) and newer stay.
        val inside = SyncTaskHistoryEntry(now.minus(Duration.ofDays(89)), listOf(t2))
        val edge = SyncTaskHistoryEntry(now.minus(Duration.ofDays(90)), listOf(t2))
        val outsideWindow = SyncTaskHistoryEntry(now.minus(Duration.ofDays(91)), listOf(t2))
        val noTime = SyncTaskHistoryEntry(null, listOf(t2.copy(doneAt = null)))
        report.check("ar1c: another PC's history is cut to 90 days on receipt (older and time-less entries go, the 90th day stays)",
            SyncTaskHistoryEntry.cut(listOf(inside, edge, outsideWindow, noTime), now) == listOf(inside, edge))
        val old = PastSettleEdits.settle(emptyList(), id(1), SyncPastSettled.DELETED, at("2026-10-01T10:00:00Z"))
        report.check("ar1c: a delete older than 30 days drops off Recently deleted", rows(old).deleted.isEmpty())
    }

    private fun noteRules(report: CheckReport) {
        val held = notes
        val edited = SessionNoteEdits.edit(held, id(21), "  typed on the work PC, edited here ", now)
        val one = edited.first { it.id == id(21) }
        report.check("ar1c: editing a note changes its text, keeps its writer and outranks the old edit", one.text == "typed on the work PC, edited here" && one.by == WORK && one.editedAt.isAfter(held[0].editedAt))
        report.check("ar1c: a blank text, an unchanged text and an unknown note change nothing", SessionNoteEdits.edit(held, id(21), "   ", now) === held && SessionNoteEdits.edit(held, id(21), "typed on the work PC", now) === held && SessionNoteEdits.edit(held, id(99), "x", now) === held)
        report.check("ar1c: a deleted note cannot be edited back", SessionNoteEdits.edit(held, id(24), "back", now) === held)
        val gone = SessionNoteEdits.delete(held, id(21), now)
        val tomb = gone.first { it.id == id(21) }
        report.check("ar1c: deleting a note keeps a tombstone with no text, stamped over the old edit", tomb.text == null && tomb.deletedAt != null && tomb.editedAt.isAfter(held[0].editedAt))
        report.check("ar1c: the edit and the delete are in what goes to the other devices (the file's notes)", SessionNoteRules.forFile(edited, now).first { it.id == id(21) }.text!!.endsWith("edited here") && SessionNoteRules.forFile(gone, now).first { it.id == id(21) }.deletedAt != null)
        report.check("ar1c: a deleted note leaves the Archive", rows(noteList = gone).notes.none { it.id == id(21) })
        report.check("ar1c: deleting an unknown or already deleted note changes nothing", SessionNoteEdits.delete(held, id(99), now) === held && SessionNoteEdits.delete(held, id(24), now) === held)

        // A PC's file against the phone's own edit: the newest edit wins either way round; a tombstone wins over an older edit.
        val pcOlder = held[0].copy(text = "PC's older text", editedAt = at("2026-11-12T09:30:00Z"))
        val pcNewer = held[0].copy(text = "PC's newer text", editedAt = now.plusSeconds(60))
        val phoneEdited = SessionNoteEdits.edit(held, id(21), "phone text", now)
        report.check("ar1c: a PC's older edit doesn't beat the phone's newer one", SessionNoteEdits.receive(phoneEdited, listOf(pcOlder), now).first { it.id == id(21) }.text == "phone text")
        report.check("ar1c: a PC's newer edit wins over the phone's", SessionNoteEdits.receive(phoneEdited, listOf(pcNewer), now.plusSeconds(120)).first { it.id == id(21) }.text == "PC's newer text")
        report.check("ar1c: the phone's tombstone beats a PC's older live copy arriving late", SessionNoteEdits.receive(gone, listOf(pcOlder), now).first { it.id == id(21) }.deletedAt != null)
        report.check("ar1c: a PC's tombstone removes the note's text here", SessionNoteEdits.receive(held, listOf(held[0].deleted(now.plusSeconds(10))), now.plusSeconds(20)).first { it.id == id(21) }.text == null)
        report.check("ar1c: an empty list from a PC leaves the notes alone, never a delete", SessionNoteEdits.receive(held, emptyList(), now).map { it.id }.toSet() == held.map { it.id }.toSet())
        report.check("ar1c: a broken entry (a live note with no text, no writer) is left out, never a delete", SessionNoteEdits.receive(held, listOf(held[0].copy(text = null), held[1].copy(by = "00000000-0000-0000-0000-000000000000")), now).first { it.id == id(21) }.text != null)
        report.check("ar1c: a note from the other PC that this phone never had is taken in", SessionNoteEdits.receive(emptyList(), held.take(3), now).size == 3)
        val window = listOf(note(50, "old", "2026-08-01T09:00:00Z", WORK), note(51, "inside", "2026-08-20T09:00:00Z", WORK))
        report.check("ar1c: notes are kept 90 days from when they were written", SessionNoteEdits.prune(window, now).map { it.id } == listOf(id(51)))
        report.check("ar1c: a tombstone is kept for the note's whole 90 days, not 30", SessionNoteEdits.prune(listOf(note(52, "x", "2026-09-20T09:00:00Z", WORK).deleted(at("2026-09-21T09:00:00Z"))), now).size == 1)

        // Plain backup, watch, logs.
        val part = SessionNotesBackup.part(held, includesNotes = true, now)
        report.check("ar1c: a password-protected backup carries the notes, and reads back the same", part != null && SessionNotesBackup.read(part).map { it.id }.toSet() == held.map { it.id }.toSet() && part.toString().contains("typed on the work PC"))
        report.check("ar1c: a plain backup carries no note part at all", SessionNotesBackup.part(held, includesNotes = false, now) == null)
        report.check("ar1c: a backup with no part reads as no notes", SessionNotesBackup.read(null).isEmpty())
    }

    private fun source(path: String): String = File(path).takeIf { it.exists() }?.readText()?.replace("\r\n", "\n") ?: ""

    private val app = "../app/src/main/java/com/accountabilibuddy/companion"

    private fun sourceChecks(report: CheckReport) {
        val screen = source("$app/ui/tasks/archive/ArchiveScreen.kt")
        val data = source("$app/ui/tasks/archive/ArchiveData.kt")
        val entry = source("$app/ui/tasks/archive/ArchiveEntryRow.kt")
        val store = source("$app/sync/SessionNotesStore.kt")
        val lists = source("$app/sync/SharedListsStore.kt")
        val shared = "../shared/src/main/java/com/accountabilibuddy/shared"
        report.check("ar1c: the new files are found", screen.isNotEmpty() && data.isNotEmpty() && entry.isNotEmpty() && store.isNotEmpty() && lists.isNotEmpty())

        // Where it sits: a row at the bottom of Tasks, Options > History stays.
        val host = source("$app/ui/tasks/page/TasksPageHost.kt")
        report.check("ar1c: the Tasks page has the Archive row at its bottom, under the pages", host.indexOf("ArchiveEntryRow(onOpen") > host.indexOf("ShelfPager(") && host.contains("ArchiveScreen(pc, now.toInstant(), controller"))
        report.check("ar1c: the round + sits above the Archive row", host.contains("bottom = 20.dp + ArchiveRowHeight"))
        val options = source("$app/ui/tasks/page/TasksOptionsSheet.kt")
        report.check("ar1c: Options > History stays (Do again, Bring back)", options.contains("OptionsPage.History ->") && options.contains("controller.bringBack(entry.id)") && options.contains("controller.doAgain(entry.id)"))

        // Past tasks go through settle records.
        fun body(signature: String) = lists.substringAfter(signature, "").substringBefore("\n    /**").substringBefore("\n    fun ")
        report.check("ar1c: tick, untick, delete and Restore of a past task go through settle records (or the keeper's copy), never the owner's history",
            body("fun tickPast(").contains("settle(") && body("fun untickPast(").contains("settle(") && body("fun deletePast(").contains("settle(") && body("fun restorePast(").contains("PastSettleEdits.restore("))
        report.check("ar1c: nothing on the phone edits a received history in place", !Regex("""history\s*=\s*lists\.history\.map""").containsMatchIn(lists) && !body("fun restorePast(").contains("history"))
        report.check("ar1c: the received history is cut to 90 days on receipt, by the shared rule", lists.substringAfter("fun receiveExtras(").substringBefore("settled?.let").contains("SyncTaskHistoryEntry.cut(history, time)"))
        report.check("ar1c: the page calls those and the History entry's Bring back for a task-list delete",
            screen.contains("app.sharedLists.tickPast(") && screen.contains("app.sharedLists.untickPast(") && screen.contains("app.sharedLists.deletePast(") && screen.contains("app.sharedLists.restorePast(row.id, it)") && screen.contains("controller.bringBack(entry)"))

        // Notes: sealed store, in the file, nowhere else.
        report.check("ar1c: the notes are stored sealed with a Keystore key, in private prefs", store.contains("TokenVault.seal(") && store.contains("TokenVault.open(") && store.contains("Context.MODE_PRIVATE") && !Regex("""\bFile\(""").containsMatchIn(store))
        val coordinator = source("$app/sync/SyncCoordinator.kt")
        val companion = source("$app/CompanionApp.kt")
        report.check("ar1c: the phone's file carries the notes, under the Sync task lists switch", coordinator.contains("sessionNotes = if (sharesTaskLists()) runCatching { sessionNotes() }.getOrNull() else null"))
        report.check("ar1c: other devices' notes merge in from a received file; a file without the part leaves them alone", companion.contains("file.sessionNotes?.let { sessionNotes.receive(it) }") && companion.contains("sync.sessionNotes = { sessionNotes.forSync() }"))
        report.check("ar1c: an edit or delete here goes out soon (the relay, folder and Drive)", companion.contains("sessionNotes.onChanged = {") && store.contains("onChanged()"))
        val watchFiles = listOf("$shared/WatchSnapshot.kt", "$shared/WatchSnapshotJson.kt") + File("../wear/src").walkTopDown().filter { it.extension == "kt" }.map { it.path.replace('\\', '/') }.toList()
        report.check("ar1c: notes never go to the watch (no note type in the snapshot or the watch app)", watchFiles.isNotEmpty() && watchFiles.none { Regex("""SessionNote|sessionNotes|SyncSessionNote""").containsMatchIn(source(it)) })
        val phoneBackups = listOf("PhoneBackup.kt", "AutoBackup.kt").map { source("$app/backup/$it") } + source("$app/sync/DeviceBackups.kt")
        report.check("ar1c: a plain phone backup and the auto-backup never touch the notes", phoneBackups.none { Regex("""sessionNotes|SessionNotes""").containsMatchIn(it) })
        val kind = source("$app/backup/PhoneKindBackup.kt")
        report.check("ar1c: only a password-protected Tasks backup gets the notes part", kind.contains("SessionNotesBackup.part(app.sessionNotes.forSync(), includesNotes,") && Regex("""sessionNotes""").findAll(kind).count() <= 4)
        val plainOnly = kind.substringAfter("fun export(").substringBefore("fun content(")
        report.check("ar1c: a backup is exported with notes only when a password is set", plainOnly.contains("includesNotes = !password.isNullOrEmpty()"))

        // Logs.
        val logging = Regex("""\b(Log\.[vdiwe]|println\(|Timber|SyncLog|syncLog)""")
        val pure = listOf("$shared/sync/SessionNoteEdits.kt", "$shared/tasks/ArchiveList.kt", "$shared/tasks/ArchiveBuilder.kt", "$shared/sync/PastSettleEdits.kt", "$shared/sync/SessionNoteRules.kt")
        report.check("ar1c: no code that holds note text logs anything", (listOf(screen, data, entry, store) + pure.map(::source)).none { logging.containsMatchIn(it) })
        report.check("ar1c: the sync log has no notes line", !source("$app/sync/SyncSendLogStore.kt").contains("sessionNotes") && !coordinator.lines().any { logging.containsMatchIn(it) && it.contains("sessionNotes") })

        // The page's UI rules.
        val clickables = screen.lines() + entry.lines()
        val clickableLines = clickables.filter { it.contains(".clickable(") }
        report.check("ar1c: every tap target on the page has a role", clickableLines.isNotEmpty() && clickableLines.all { it.contains("role = Role.") })
        report.check("ar1c: every tap target is at least 48 dp high",
            clickables.indices.filter { clickables[it].contains(".clickable(") }.all { index ->
                val window = clickables.subList((index - 2).coerceAtLeast(0), index + 1).joinToString(" ")
                window.contains("size(48.dp)") || window.contains("heightIn(min = 48.dp)") || window.contains("heightIn(min = 56.dp)") || window.contains("height(ArchiveRowHeight)")
            })
        report.check("ar1c: every icon-only tap target has a spoken name",
            clickableLines.filter { it.contains("size(48.dp)") }.all { it.contains("contentDescription") })
        report.check("ar1c: the page takes its colours and type from the theme (no literal colours)", !Regex("""Color\(0x|Color\.(Red|Blue|Green|Black|White)""").containsMatchIn(screen + entry))
        report.check("ar1c: back closes the Archive, and the header has a Back button named", screen.contains("BackHandler(onBack = onBack)") && screen.contains("\"Back to Tasks\""))
        report.check("ar1c: a delete of a note or a come-back asks first (a note says it reaches every device)", screen.contains("confirming = item") && screen.contains("item.deleteWarning"))
        val list = source("$shared/tasks/ArchiveList.kt")
        report.check("ar1c: the page's times are formatted 12-hour with AM/PM in one place", list.contains("\"M/d h:mm a\"") && !Regex("""HH:mm|\bH:mm|\"kk""").containsMatchIn(list))
        report.check("ar1c: the page reaches the builder through the shared list assembly", data.contains("ArchiveBuilder.build(") && data.contains("ArchiveList.items(") && screen.contains("ArchiveList.shown(") && screen.contains("ArchiveList.deleted("))
        report.check("ar1c: the page shows the same Duration constants as the PC (30 days deleted, 90 listed)", ArchiveBuilder.DELETED_LISTED == Duration.ofDays(30) && ArchiveBuilder.LISTED == Duration.ofDays(90))
        report.check("ar1c: the page reads the other devices' kept copies only through the received list (no raw file reads)", !screen.contains("sync-received") && !Regex("""\bFile\(""").containsMatchIn(data))
    }
}
