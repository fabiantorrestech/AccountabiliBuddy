package com.accountabilibuddy.companion.debug

import android.content.Context
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.model.LocalWindow
import com.accountabilibuddy.companion.placement.PlacementSession
import com.accountabilibuddy.companion.placement.PlacementState
import com.accountabilibuddy.shared.sync.SyncDeviceCard
import com.accountabilibuddy.shared.sync.SyncFileCodec
import com.accountabilibuddy.shared.sync.SyncKeyCode
import java.time.Duration
import java.time.Instant
import java.time.LocalDate
import java.time.LocalTime

/**
 * Debug builds only. Puts made-up data into the app through the paths real data takes, so the screens treat it as
 * received:
 *
 * - A fake "Work PC" ([DemoPcFile]) is sealed with the group's key and handed to [com.accountabilibuddy.companion.sync.SyncCoordinator.acceptSealed],
 *   the same call a QR scan, a folder or Drive makes. That is what brings the PC's profiles and day plan (imported by the app's own
 *   settings path), the running session with its tasks, the come-backs, Later items and next tasks (merged into the shared lists)
 *   and sixty days of sessions and drift.
 * - A sleep night per day, written to the store the Health Connect reader writes.
 * - In `session` mode, the placement record of the running session (the phone's out-of-hand count).
 *
 * Phone pickups can't be faked here: they are counted from Android's usage records, which no app store holds. The placement
 * record's out count is the nearest thing the app keeps.
 *
 * It only works on a phone that is in no sync group or in the one it made itself, so it can't mix with real devices.
 */
class DemoSeeder(private val app: CompanionApp) {
    private val prefs = app.getSharedPreferences(PREFS, Context.MODE_PRIVATE)

    /** Runs [mode] ([DemoSeedReceiver.MODE_IDLE], [DemoSeedReceiver.MODE_SESSION] or [DemoSeedReceiver.MODE_CLEAR]); returns a line for logcat. */
    fun run(mode: String): String = when (mode) {
        DemoSeedReceiver.MODE_IDLE -> seed(withSession = false)
        DemoSeedReceiver.MODE_SESSION -> seed(withSession = true)
        DemoSeedReceiver.MODE_PLANNING -> seed(withSession = true, planning = true)
        // A session on Work PC while a second, idle PC (Desk PC) wrote the newest file: the controlled PC is the idle one.
        // Stepped away for 3 minutes (the timer holds); and a running session on a Budget day (the pace line).
        DemoSeedReceiver.MODE_AWAY -> seed(withSession = true, away = true)
        // Stepped away across the old end (the phone must still see the session), and a file 40 minutes old while away (it must not).
        DemoSeedReceiver.MODE_AWAY_PAST -> seed(withSession = true, away = true, pastOldEnd = true)
        DemoSeedReceiver.MODE_AWAY_STALE -> seed(withSession = true, away = true, fileAgeMinutes = 40)
        // Stepped away with the old end still ahead and a file 40 minutes old (the PC went offline): the session must still hold.
        DemoSeedReceiver.MODE_AWAY_OFFLINE -> seed(withSession = true, away = true, fileAgeMinutes = 40, longSession = true)
        DemoSeedReceiver.MODE_SESSION_BUDGET -> seed(withSession = true, dayKind = "budget")
        DemoSeedReceiver.MODE_TWOPC -> seed(withSession = true, secondPc = true)
        // Idle days of another kind (ADR 47): the headline wording on a Budget day, a Free day and a day off.
        DemoSeedReceiver.MODE_BUDGET, DemoSeedReceiver.MODE_FREE, DemoSeedReceiver.MODE_DAYOFF -> seed(withSession = false, dayKind = mode)
        DemoSeedReceiver.MODE_CLEAR -> clear()
        DemoSeedReceiver.MODE_PLAN -> seed(withSession = false, dayKind = "week").also { seedEvents() }
        DemoSeedReceiver.MODE_PLAN_SESSION -> seed(withSession = true, dayKind = "week").also { seedEvents() }
        DemoSeedReceiver.MODE_PLAN_WAIT -> holdPlanEdit()
        DemoSeedReceiver.MODE_TASKS -> TasksDemo(app).seed(seed(withSession = false))
        DemoSeedReceiver.MODE_TASKS_SESSION -> TasksDemo(app).seed(seed(withSession = true))
        // Sharing-folder checks (folder-repoint fix): a new Work PC file into the chosen folder, and the folder's "on since" 15 minutes back.
        DemoSeedReceiver.MODE_FOLDER_DROP -> dropInFolder()
        DemoSeedReceiver.MODE_FOLDER_AGE -> ageFolder()
        DemoSeedReceiver.MODE_FOLDER_UPGRADE -> simulateUpgrade()
        DemoSeedReceiver.MODE_DUMP -> TasksDemo(app).dump()
        DemoSeedReceiver.MODE_MERGE_CHECK -> TasksDemo(app).mergeCheck()
        DemoSeedReceiver.MODE_HANDOFF_CHECK -> TasksDemo(app).handoffCheck()
        DemoSeedReceiver.MODE_DONE_CHECK -> TasksDemo(app).doneCheck()
        else -> "unknown mode \"$mode\" (idle, session or clear)"
    }

    private fun seed(withSession: Boolean, planning: Boolean = false, dayKind: String = "schedule", secondPc: Boolean = false, away: Boolean = false, pastOldEnd: Boolean = false, fileAgeMinutes: Long = 0, longSession: Boolean = false): String {
        if (app.sync.isJoined && !prefs.getBoolean(KEY_JOINED_BY_DEMO, false)) {
            return "refused: this phone is in a real sync group; the seeder only works in none or its own"
        }

        if (!app.sync.isJoined) {
            if (!app.sync.join(SyncKeyCode.newCode())) {
                return "could not start the demo group"
            }

            prefs.edit().putBoolean(KEY_JOINED_BY_DEMO, true).apply()
        }

        val key = app.sync.key.value ?: return "no sync key"
        // [fileAgeMinutes] > 0 writes everything that long ago (a file the PC has not rewritten since).
        val now = Instant.now().minus(Duration.ofMinutes(fileAgeMinutes))
        val file = DemoPcFile.build(now, withSession, listOf(SyncDeviceCard.METHOD_QR), planning, dayKind, away, pastOldEnd, longSession)
        val error = app.sync.acceptSealed(SyncFileCodec.seal(file, key), SyncDeviceCard.METHOD_QR)
        if (error != null) {
            return "the demo file was refused: $error"
        }

        if (secondPc) {
            // An idle PC with no history of its own, a moment newer than Work PC's file.
            val other = DemoPcFile.build(now, false, listOf(SyncDeviceCard.METHOD_QR))
            val desk = other.copy(
                device = other.device.copy(deviceId = DemoPcFile.SECOND_DEVICE_ID, name = "Desk PC", writtenAt = now.plusSeconds(2)),
                sessions = emptyList(), drift = emptyList(), running = null,
            )
            val problem = app.sync.acceptSealed(SyncFileCodec.seal(desk, key), SyncDeviceCard.METHOD_QR)
            if (problem != null) return "the second demo file was refused: $problem"
        }

        app.store.setSlept(sleepNights())
        if (withSession) {
            seedPlacement(now)
        }

        return "seeded ${if (withSession) "session" else "idle"}: ${file.sessions.size} sessions, ${file.drift.size} drift marks"
    }

    // What a sync app does for a shared folder: the Work PC's newest file appears in it (the phone reads it at its next pass or "Sync now").
    private fun dropInFolder(): String {
        val key = app.sync.key.value ?: return "no sync key"
        val tree = app.sync.settings.value.folderUri?.let(android.net.Uri::parse) ?: return "no folder chosen"
        val sealed = SyncFileCodec.seal(DemoPcFile.build(Instant.now(), false, listOf(SyncDeviceCard.METHOD_FOLDER)), key)
        val name = com.accountabilibuddy.shared.sync.SyncFolder.fileNameFor(key.groupId, DemoPcFile.DEVICE_ID)
        val treeId = android.provider.DocumentsContract.getTreeDocumentId(tree)
        val children = android.provider.DocumentsContract.buildChildDocumentsUriUsingTree(tree, treeId)
        var existing: android.net.Uri? = null
        app.contentResolver.query(children, arrayOf("document_id", "_display_name"), null, null, null)?.use { cursor ->
            while (cursor.moveToNext()) {
                if (cursor.getString(1) == name) existing = android.provider.DocumentsContract.buildDocumentUriUsingTree(tree, cursor.getString(0))
            }
        }

        val target = existing ?: android.provider.DocumentsContract.createDocument(
            app.contentResolver, android.provider.DocumentsContract.buildDocumentUriUsingTree(tree, treeId), "application/octet-stream", name,
        ) ?: return "could not create the file"
        app.contentResolver.openOutputStream(target, "wt")?.use { it.write(sealed) }
        return "dropped the Work PC's file in the folder as $name"
    }

    // The state an update from the version before the "delivered" rule leaves: the Work PC's file held as having come by the folder, the folder
    // on for 15 minutes, and no delivered or carried marks. Restart the app afterwards (the migration runs at start-up).
    private fun simulateUpgrade(): String {
        val key = app.sync.key.value ?: return "no sync key"
        val file = DemoPcFile.build(Instant.now(), false, listOf(SyncDeviceCard.METHOD_FOLDER))
        val error = app.sync.acceptSealed(SyncFileCodec.seal(file, key), SyncDeviceCard.METHOD_FOLDER)
        if (error != null) return "refused: $error"

        val prefs = app.getSharedPreferences("syncOff", Context.MODE_PRIVATE)
        val editor = prefs.edit()
        prefs.all.keys.filter { it.startsWith("delivered.") || it.startsWith("out.") || it == "deliveryMigrated" }.forEach { editor.remove(it) }
        editor.putLong("since.folder", Instant.now().minus(Duration.ofMinutes(15)).toEpochMilli()).commit()
        return "upgrade state: file held by the folder, no marks; restart the app"
    }

    // The shared folder has been on, unchanged, for 15 minutes (the wait's "on since"), without waiting for real.
    private fun ageFolder(): String {
        app.getSharedPreferences("syncOff", Context.MODE_PRIVATE).edit().putLong("since.folder", Instant.now().minus(Duration.ofMinutes(15)).toEpochMilli()).apply()
        return "the folder counts as on for 15 minutes"
    }

    // The standup and a review today, an all-day event tomorrow (the Calendar rows of the Plan timeline).
    private fun seedEvents() {
        val today = LocalDate.now()
        fun event(id: String, title: String, day: LocalDate, from: LocalTime?, to: LocalTime?) = kotlinx.serialization.json.buildJsonObject {
            put("id", kotlinx.serialization.json.JsonPrimitive(id))
            put("calendarId", kotlinx.serialization.json.JsonPrimitive("demo"))
            put("title", kotlinx.serialization.json.JsonPrimitive(title))
            put("localStart", kotlinx.serialization.json.JsonPrimitive(day.atTime(from ?: LocalTime.MIDNIGHT).toString()))
            put("localEnd", kotlinx.serialization.json.JsonPrimitive((if (to != null) day.atTime(to) else day.plusDays(1).atStartOfDay()).toString()))
            put("isAllDay", kotlinx.serialization.json.JsonPrimitive(from == null))
        }

        val events = kotlinx.serialization.json.JsonArray(
            listOf(
                event("demo-1", "Standup", today, LocalTime.of(14, 30), LocalTime.of(15, 0)),
                event("demo-2", "Design review", today, LocalTime.of(10, 0), LocalTime.of(10, 45)),
                event("demo-3", "Team offsite", today.plusDays(1), null, null),
            ),
        )
        app.store.eventsFromFile(events, Instant.now())
    }

    // A plan edit the PC took and holds for 10 minutes: what its "applied" answer says (ADR 28), fed through the phone's own parser.
    private fun holdPlanEdit(): String {
        app.planFileSync.holdFrom(
            "On the PC: Waits 10 min: it frees part of today's work hours (work hours on this computer). The banner counts down and can cancel it.",
            "Work PC", Instant.now(),
        )
        return "plan edit held for 10 minutes"
    }

    private fun clear(): String {
        val now = Instant.now()
        DemoPcFile.PARKED_IDS.forEach(app.sharedLists::deleteParked)
        DemoPcFile.NEXT_TASK_IDS.forEach(app.sharedLists::deleteNextTask)
        DemoPcFile.LATER_IDS.forEach { app.sharedLists.deleteLater(it) }
        TasksDemo(app).clearLists()
        app.store.setSlept(emptyList())
        app.placementStore.end(now)
        // Leaving forgets every received device; it is only done for the group the seeder made.
        val madeGroup = prefs.getBoolean(KEY_JOINED_BY_DEMO, false)
        if (madeGroup) {
            // Leaving is locked while a PC session runs (no escape hatches): the demo PC "ends" its session first, as a real one's next file would.
            app.sync.key.value?.let { key ->
                app.sync.acceptSealed(SyncFileCodec.seal(DemoPcFile.build(now, false, listOf(SyncDeviceCard.METHOD_QR)), key), SyncDeviceCard.METHOD_QR)
            }

            // The app applies that file's settings in the background; let it finish so the clean-up below is the last word.
            Thread.sleep(SETTLE_MS)
            val problem = app.sync.leave()
            if (problem != null) {
                return "could not leave the demo group: $problem"
            }

            prefs.edit().putBoolean(KEY_JOINED_BY_DEMO, false).apply()
        }

        Thread.sleep(SETTLE_MS)
        app.store.clearImport()
        app.getSharedPreferences("pcFiles", Context.MODE_PRIVATE).edit().clear().apply()
        return "cleared" + if (madeGroup) " (left the demo group)" else " (the phone's own group was left alone; the demo PC's copy stays)"
    }

    // The last seven nights, 11:20 PM to about 6:45 AM, a little different each night.
    private fun sleepNights(): List<LocalWindow> {
        val today = LocalDate.now()
        return (7 downTo 1).map { back ->
            val start = today.minusDays(back.toLong()).atTime(LocalTime.of(23, 20)).plusMinutes((back * 7 % 25).toLong())
            LocalWindow(start, start.plus(Duration.ofMinutes(7 * 60L + 15 + (back * 11 % 40))))
        }
    }

    // The running session's placement: face up on the desk, with a few times the phone was picked up.
    private fun seedPlacement(now: Instant) {
        val start = now.minus(Duration.ofMinutes(25))
        app.placementStore.begin(PlacementSession.Info(start, authoritative = true, onBreak = false), now)
        app.placementStore.reading(PlacementState.Desk, now.minus(Duration.ofMinutes(1)))
        repeat(3) { app.placementStore.addOut() }
    }

    private companion object {
        const val PREFS = "demoSeed"
        const val KEY_JOINED_BY_DEMO = "joinedByDemo"
        const val SETTLE_MS = 2_000L
    }
}
