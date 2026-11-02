package com.accountabilibuddy.companion.debug

import com.accountabilibuddy.companion.data.DayPlanJson
import com.accountabilibuddy.companion.model.DayPlan
import com.accountabilibuddy.shared.sync.SessionTaskItem
import com.accountabilibuddy.shared.sync.SyncComeback
import com.accountabilibuddy.shared.sync.SyncDayPlan
import com.accountabilibuddy.shared.sync.SyncDeviceCard
import com.accountabilibuddy.shared.sync.SyncDocument
import com.accountabilibuddy.shared.sync.SyncDrift
import com.accountabilibuddy.shared.sync.SyncFile
import com.accountabilibuddy.shared.sync.SyncLaterItem
import com.accountabilibuddy.shared.sync.SyncNextTask
import com.accountabilibuddy.shared.sync.SyncParked
import com.accountabilibuddy.shared.sync.SyncProfileOption
import com.accountabilibuddy.shared.sync.SyncRunning
import com.accountabilibuddy.shared.sync.SyncSession
import kotlinx.serialization.json.buildJsonArray
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.put
import java.time.DayOfWeek
import java.time.Duration
import java.time.Instant
import java.time.LocalDate
import java.time.LocalTime
import java.time.ZoneId
import java.util.UUID
import kotlin.random.Random

/**
 * The made-up sync file of the demo "Work PC" (debug builds only): sixty days of sessions and drift, today's day plan,
 * come-backs, the Later list, the next session's tasks and, for [withSession], a running session with three tasks.
 * Every name is generic; nothing here is the user's real data. The same device id every time, so a second seed replaces
 * the first one's copy the way a real PC's next file does.
 */
object DemoPcFile {
    val DEVICE_ID: UUID = UUID.nameUUIDFromBytes("demo-work-pc".toByteArray())

    private const val DAYS_OF_HISTORY = 60L

    // Newer than every version check the phone makes (remote control, noise, task edits), so the cards offer their buttons.
    private const val PC_VERSION = "0.1.200"
    private const val DEMO_PC_NAME = "Work PC"
    private val DEEP_WORK_ID: UUID = UUID.nameUUIDFromBytes("demo-profile-deep".toByteArray())
    private val ADMIN_ID: UUID = UUID.nameUUIDFromBytes("demo-profile-admin".toByteArray())
    private val READING_ID: UUID = UUID.nameUUIDFromBytes("demo-profile-reading".toByteArray())

    /** The ids of the demo's shared list items (36-character ids, as the sync file requires), so `clear` can delete them. */
    val PARKED_IDS = (1..4).map { UUID.nameUUIDFromBytes("demo-parked-$it".toByteArray()).toString() }
    val NEXT_TASK_IDS = (1..2).map { UUID.nameUUIDFromBytes("demo-next-$it".toByteArray()).toString() }
    val LATER_IDS = (1..3).map { UUID.nameUUIDFromBytes("demo-later-$it".toByteArray()).toString() }

    /** Every day follows the Workday template, so "today" always has a plan, whatever the weekday. */
    val DAY_PLAN: DayPlan = DayPlan.fromWorkdayAndOffDay(
        DayPlan.Default.categories, DayOfWeek.entries.toSet(), DayPlan.Default.workday, DayPlan.Default.workday,
    )

    fun build(now: Instant, withSession: Boolean, methods: List<String>): SyncFile {
        val sessions = history(now).toMutableList()
        val running = if (withSession) {
            val start = now.minus(Duration.ofMinutes(25))
            sessions += SyncSession(start, null, "Deep work", false, runningTasks(now))
            SyncRunning("Deep work", start, start.plus(Duration.ofMinutes(50)), false, null, currentTask = "demo-task-2", profileId = DEEP_WORK_ID.toString())
        } else {
            null
        }

        val planEditedAt = LocalDate.now().atStartOfDay(ZoneId.systemDefault()).toInstant()
        val parked = parked(now)
        return SyncFile(
            formatVersion = SyncFile.CURRENT_FORMAT_VERSION,
            device = SyncDeviceCard(DEVICE_ID, DEMO_PC_NAME, SyncDeviceCard.KIND_PC, PC_VERSION, now, methods, false, emptyMap()),
            sessions = sessions,
            drift = drift(sessions),
            dayPlan = SyncDayPlan(DayPlanJson.write(DAY_PLAN).toString(), "demo-plan-1", planEditedAt, "demo-plan-1"),
            documents = listOf(SyncDocument(SyncDocument.PROFILES_KEY, profilesJson(), planEditedAt)),
            coversFrom = now.minus(Duration.ofDays(DAYS_OF_HISTORY)),
            comebacks = parked.filter { it.doneAt == null }.map { SyncComeback(it.id, it.text, it.createdAt) },
            running = running,
            goalMinutes = 240,
            profileOptions = listOf(
                SyncProfileOption(DEEP_WORK_ID.toString(), "Deep work"),
                SyncProfileOption(ADMIN_ID.toString(), "Admin"),
                SyncProfileOption(READING_ID.toString(), "Reading"),
            ),
            defaultProfileId = DEEP_WORK_ID.toString(),
            nextTasks = nextTasks(now),
            parked = parked,
            later = later(now),
        )
    }

    // Weekday sessions, two or three a day, kept the same on every run by a fixed seed.
    private fun history(now: Instant): List<SyncSession> {
        val zone = ZoneId.systemDefault()
        val random = Random(2026)
        val today = LocalDate.now(zone)
        val profiles = listOf("Deep work", "Admin", "Reading")
        val sessions = mutableListOf<SyncSession>()
        for (back in DAYS_OF_HISTORY downTo 0L) {
            val day = today.minusDays(back)
            val weekend = day.dayOfWeek == DayOfWeek.SATURDAY || day.dayOfWeek == DayOfWeek.SUNDAY
            if (weekend && random.nextInt(4) != 0) {
                continue
            }

            val count = if (weekend) 1 else 2 + random.nextInt(2)
            var startMinute = 9 * 60 + random.nextInt(45)
            repeat(count) { index ->
                val length = 25 + random.nextInt(66)
                val start = day.atTime(LocalTime.of(startMinute / 60, startMinute % 60)).atZone(zone).toInstant()
                val end = start.plus(Duration.ofMinutes(length.toLong()))
                // Today only holds the sessions that are over by now, leaving room for the running one.
                if (end.isBefore(now.minus(Duration.ofMinutes(45)))) {
                    sessions += SyncSession(start, end, profiles[(back.toInt() + index) % profiles.size], false)
                }

                startMinute += length + 45 + random.nextInt(60)
            }
        }

        // One session that was added by hand, as History allows.
        val addedStart = today.minusDays(3).atTime(8, 0).atZone(zone).toInstant()
        sessions += SyncSession(addedStart, addedStart.plus(Duration.ofMinutes(40)), null, true)

        // A real day always has a finished session before the running one.
        val earlierToday = now.minus(Duration.ofHours(2)).minus(Duration.ofMinutes(30))
        if (sessions.none { it.start.isAfter(earlierToday.minus(Duration.ofMinutes(1))) }) {
            sessions += SyncSession(earlierToday, earlierToday.plus(Duration.ofMinutes(55)), "Admin", false)
        }

        return sessions.sortedBy { it.start }
    }

    // One to three stretches of drift in two out of three sessions: generic apps and sites only.
    private fun drift(sessions: List<SyncSession>): List<SyncDrift> {
        val random = Random(7)
        val sites = listOf("news.example.com", "video.example.com", "social.example.org")
        val marks = mutableListOf<SyncDrift>()
        sessions.filter { it.end != null && !it.added }.forEachIndexed { index, session ->
            if (index % 3 == 0) {
                return@forEachIndexed
            }

            val length = Duration.between(session.start, session.end)
            repeat(1 + random.nextInt(3)) {
                val offset = random.nextLong(length.toMinutes().coerceAtLeast(10) - 8)
                val start = session.start.plus(Duration.ofMinutes(offset))
                val end = minOf(start.plus(Duration.ofMinutes(2L + random.nextInt(6))), session.end!!)
                val site = if (random.nextBoolean()) sites[random.nextInt(sites.size)] else null
                marks += SyncDrift(start, end, if (site != null) "browser.exe" else "game.exe", site, null)
            }
        }

        return marks.sortedBy { it.start }
    }

    // Three tasks: the first is done, the second has steps, the third is open (the shown one is the second).
    private fun runningTasks(now: Instant): List<SessionTaskItem> = listOf(
        SessionTaskItem("demo-task-1", "Write the status update", now.minus(Duration.ofMinutes(12)), emptyList(), "demo-cat-writing", "Writing", "#7C3AED"),
        SessionTaskItem(
            "demo-task-2", "Review the open requests", null,
            listOf(
                SessionTaskItem("demo-step-1", "Sort by oldest", now.minus(Duration.ofMinutes(4)), emptyList()),
                SessionTaskItem("demo-step-2", "Answer the three easiest", null, emptyList()),
                SessionTaskItem("demo-step-3", "Flag the rest for tomorrow", null, emptyList()),
            ),
            "demo-cat-admin", "Admin", "#CA8A04",
        ),
        SessionTaskItem("demo-task-3", "Tidy the project notes", null, emptyList(), "demo-cat-admin", "Admin", "#CA8A04"),
    )

    private fun parked(now: Instant): List<SyncParked> = listOf(
        SyncParked(PARKED_IDS[0], "Reply to the supplier about the delivery date", now.minus(Duration.ofHours(20)), null, now, null, 1.0),
        SyncParked(PARKED_IDS[1], "Look up the printer manual", now.minus(Duration.ofHours(6)), null, now, null, 2.0),
        SyncParked(PARKED_IDS[2], "Book a dentist appointment", now.minus(Duration.ofHours(30)), now.minus(Duration.ofHours(3)), now, null, 3.0),
        SyncParked(PARKED_IDS[3], "Rename the shared folder", now.minus(Duration.ofMinutes(50)), null, now, null, 4.0),
    )

    /** The Later items as tombstones (deleted just now), for `clear`. */
    fun laterTombstones(now: Instant): List<SyncLaterItem> = later(now).map { it.deleted(now) }

    private fun nextTasks(now: Instant): List<SyncNextTask> = listOf(
        SyncNextTask(NEXT_TASK_IDS[0], "Draft the weekly summary", null, "Writing", "#7C3AED", null, now, null, 1.0, estimateMinutes = 45),
        SyncNextTask(NEXT_TASK_IDS[1], "Review the open tickets", null, "Admin", "#CA8A04", null, now, null, 2.0),
    )

    private fun later(now: Instant): List<SyncLaterItem> {
        val edited = now
        return listOf(
            SyncLaterItem(
                LATER_IDS[0], "Tidy the shared drive",
                listOf(SessionTaskItem("demo-later-step-1", "Archive last year's folders", null, emptyList()), SessionTaskItem("demo-later-step-2", "Delete duplicates", null, emptyList())),
                "Admin", "#CA8A04", 60, null, null, edited, null, 1.0,
            ),
            SyncLaterItem(LATER_IDS[1], "Plan the quarterly review", null, "Writing", "#7C3AED", 90, null, LocalDate.now().plusDays(5).toString(), edited, null, 2.0),
            SyncLaterItem(LATER_IDS[2], "Read the onboarding guide", null, null, null, null, null, null, edited, null, 3.0),
        )
    }

    // The PC's profile document (schema 13 as the phone reads it); the day plan is added from the file's plan.
    private fun profilesJson(): String = buildJsonObject {
        put("schemaVersion", 13)
        put("profiles", buildJsonArray {
            for ((id, name) in listOf(DEEP_WORK_ID to "Deep work", ADMIN_ID to "Admin", READING_ID to "Reading")) {
                add(buildJsonObject {
                    put("id", id.toString())
                    put("name", name)
                    put("rules", buildJsonArray {
                        add(buildJsonObject {
                            put("id", UUID.nameUUIDFromBytes("demo-rule-$name".toByteArray()).toString())
                            put("target", "processName")
                            put("kind", "exact")
                            put("pattern", "notes.exe")
                            put("verdict", "allow")
                        })
                    })
                    put("unknownGracePeriod", "00:00:30")
                    put("unlockOptions", buildJsonArray {
                        add(buildJsonObject {
                            put("id", UUID.nameUUIDFromBytes("demo-unlock-$name".toByteArray()).toString())
                            put("name", "Wait 5 minutes")
                            put("wait", "00:05:00")
                        })
                    })
                    put("askAboutUnlistedSites", true)
                })
            }
        })
    }.toString()
}
