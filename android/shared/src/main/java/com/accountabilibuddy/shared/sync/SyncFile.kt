package com.accountabilibuddy.shared.sync

import java.time.Instant
import java.util.UUID

/**
 * One device's sync file (ADR 22), as on the PC (Core's SyncFile): its card, the last 14 days of its counted focus
 * sessions and drift, its day plan and its shared settings. Each device writes only its own file; a receiver replaces
 * its copy of that device's part. Only readable fields (ADR 15): never titles, addresses, OCR text, reasons or an added
 * session's note. Sealed and opened by [SyncFileCodec].
 *
 * @property formatVersion [CURRENT_FORMAT_VERSION] when written; a reader refuses a newer one.
 * @property drift Distracting stretches inside the sessions.
 * @property dayPlan Null when the file carries no plan.
 * @property documents The shared settings (profiles, sleep, day bar).
 * @property coversFrom The time the file speaks from: it holds every counted session of the device that ends after it,
 * and the drift in them, so a receiver drops what it has from the device after it ([SyncMerge]). Null in a file that
 * doesn't say (then [SyncMerge.coveredFrom] reads it from the sessions). Optional in the JSON: written only when set,
 * so a format 1 file without it reads as before.
 */
data class SyncFile(
    val formatVersion: Int,
    val device: SyncDeviceCard,
    val sessions: List<SyncSession>,
    val drift: List<SyncDrift>,
    val dayPlan: SyncDayPlan?,
    val documents: List<SyncDocument>,
    val coversFrom: Instant? = null,
    /** The PC's open come-backs (ADR 30); null for none or an older app. */
    val comebacks: List<SyncComeback>? = null,
    /** A phone's pickups during the other devices' sessions (ADR 26); null for none or an older app. */
    val pickups: List<SyncPickup>? = null,
    /** Today's flexible lunch as this device started or took it (schema v8); null for none or an older app. */
    val lunch: SyncLunch? = null,
    /** What the user did on this phone for a PC (ADR 26), kept until that PC acknowledges it; null for none. */
    val actions: List<SyncAction>? = null,
    /** The action ids a PC applied (ADR 26); null for none. */
    val acks: List<String>? = null,
    /** A PC's running session (ADR 26), so the countdown works with Drive and no link; null when none runs. */
    val running: SyncRunning? = null,
    /** A PC's calendar events (ADR 26), in the link's settings shape; null when not sent. */
    val events: kotlinx.serialization.json.JsonArray? = null,
    /** A phone's smart bedtime (ADR 34): derived numbers per kind of night; null when it's off. */
    val smartBedtime: SyncSmartBedtime? = null,
    /** Bedtime reminders dismissed or snoozed on this device in the last day (ADR 38); null for none. */
    val reminderActions: List<com.accountabilibuddy.shared.ReminderMessages.Action>? = null,
    /** This device's blocks and whether each is on (ADR 37); null when it has no blocker. */
    val blockers: List<SyncBlocker>? = null,
    /** A phone's estimated wake time per kind of morning (ADR 40): only the two times; null when it has none. */
    val wakeEstimate: SyncWakeEstimate? = null,
    /** A PC's derived summary of the user's Beeminder goals (ADR 43); null without a token there or from an older app. */
    val beeminder: List<SyncBeeminderGoal>? = null,
) {
    companion object {
        const val CURRENT_FORMAT_VERSION = 1
    }
}

/**
 * Who wrote a sync file, when, and the newest data it holds from the other devices (for the Devices tab).
 *
 * @property name The device's own name. Never part of a file name (ADR 22).
 * @property kind [KIND_PC], [KIND_PHONE] or [KIND_WATCH].
 * @property writtenAt When the device wrote the file; a running session's end is taken as this time.
 * @property methods The methods this device uses: [METHOD_LINK], [METHOD_FOLDER], [METHOD_QR], [METHOD_FILE].
 * @property historyOff Activity history is off on the device: the file carries no sessions or drift, and receivers
 * delete the ones they have from it (ADR 15).
 * @property seen For each other device (by id), the newest [writtenAt] this device holds from it.
 */
data class SyncDeviceCard(
    val deviceId: UUID,
    val name: String,
    val kind: String,
    val appVersion: String,
    val writtenAt: Instant,
    val methods: List<String>,
    val historyOff: Boolean,
    val seen: Map<UUID, Instant>,
    /** The devices linked to this one (the phone's watch, Task 18); empty for none. Optional in the JSON. */
    val companions: List<SyncCompanion> = emptyList(),
    /** A phone's next alarm, which the user's PCs can take as the wake time (ADR 23); null for none. Optional in the JSON. */
    val nextAlarm: Instant? = null,
) {
    companion object {
        const val KIND_PC = "pc"
        const val KIND_PHONE = "phone"
        const val KIND_WATCH = "watch"

        /** The network link to a paired PC (ADR 14). */
        const val METHOD_LINK = "link"

        /** A sync folder the user picks, e.g. inside Google Drive for desktop. */
        const val METHOD_FOLDER = "folder"

        /** QR codes the PC shows and the phone scans (or a picture of the phone's codes). */
        const val METHOD_QR = "qr"

        /** A sync file the user shares or imports by hand. */
        const val METHOD_FILE = "file"

        /** The app's own Google Drive folder (ADR 26). */
        const val METHOD_DRIVE = "drive"
    }
}

/**
 * A focus session that counts on the device that wrote the file.
 *
 * @property end Null for the session that was running when the file was written ([SyncDeviceCard.writtenAt]).
 * @property profile The profile's name; null for a session added by hand.
 * @property added Added by hand in History (ADR 20); its note never travels.
 */
data class SyncSession(
    val start: Instant,
    val end: Instant?,
    val profile: String?,
    val added: Boolean,
    /** Its guided-session checklist (ADR 24); empty for none. Optional in the JSON. */
    val tasks: List<SessionTaskItem> = emptyList(),
)

/**
 * A distracting stretch inside a counted session, from the readable columns only (ADR 8, 17).
 *
 * @property app The app's process name (the package name on the phone).
 * @property site The site's domain, when it was a page in a browser.
 * @property section The site's section (ADR 17), e.g. "r/manybaggers".
 */
data class SyncDrift(val start: Instant, val end: Instant, val app: String, val site: String?, val section: String?)

/**
 * A device's day plan as it travels (ADR 19, 22), with what the clash rule ([DayPlanClash]) needs: its version, when it
 * was last edited, and the newest version the device shares with the others. A device stamps its own plan with the
 * functions below, as on the PC, so several edits between two files still read as one device's change.
 *
 * @property planJson The plan in the PC's day plan shape (the PC's ProfileDocumentSerializer.SerializeDayPlan).
 * @property version The PC's DayPlanVersion of the plan.
 * @property editedAt When the plan was last edited, on whichever device; it travels with the plan. A plan nobody has
 * edited (a new device's default) can say the earliest time, so any edited plan is newer.
 * @property basedOn The newest version the device shares with the others: the version it last took from another device
 * or saw in another device's file (then it equals [version]), or, after edits here, the shared version they started
 * from. Further edits don't move it. Null when it isn't known (a plan edited before the device joined): it counts as
 * edited.
 */
data class SyncDayPlan(val planJson: String, val version: String, val editedAt: Instant, val basedOn: String?) {
    /** True when the device edited the plan since it last took or shared one (or that isn't known). */
    fun hasLocalEdits(): Boolean = basedOn != version

    /** The plan after an edit on this device: [basedOn] stays, however many edits come before the next file. */
    fun edited(planJson: String, version: String, editedAt: Instant): SyncDayPlan =
        copy(planJson = planJson, version = version, editedAt = editedAt)

    /** The plan once shared: taken from another device or found in its file. It is based on its own version. */
    fun shared(): SyncDayPlan = copy(basedOn = version)

    /**
     * This device's plan kept over [other] (after "Keep the one I was using", or when it was the latest): it reads as an
     * edit of the other plan, so the other device takes it without asking.
     */
    fun keptOver(other: SyncDayPlan): SyncDayPlan = copy(basedOn = other.version)
}

/**
 * A shared setting as it travels, with when it was last edited: the newest edit wins, and each receiver applies it by
 * its own rules (a looser profile change still waits). Cold Turkey's expected blocks are never one (ADR 22).
 *
 * @property key [PROFILES_KEY], [SLEEP_KEY] or [DAY_BAR_KEY].
 * @property json The setting's JSON as the writing device stores it.
 */
data class SyncDocument(val key: String, val json: String, val editedAt: Instant) {
    companion object {
        const val PROFILES_KEY = "profiles"
        const val SLEEP_KEY = "sleep"
        const val DAY_BAR_KEY = "daybar"
    }
}

/**
 * A device linked to the one that wrote the file (ADR 22, Task 18): the phone's Pixel Watch, shown under its device on
 * the PC's Devices tab, display only (paired and unpaired on the phone), as on the PC (Core's SyncCompanion).
 *
 * @property linked Still known to the writing device (its watch link is still valid).
 * @property lastHeard When the writing device last heard from it or delivered to it; null when not yet.
 */
data class SyncCompanion(val name: String, val kind: String, val linked: Boolean, val lastHeard: Instant?) {
    companion object {
        const val KIND_WATCH = "watch"
    }
}
/** A phone's estimated wake time (ADR 40) for workday and Off-Day mornings, each "HH:mm"; null for a kind without enough data. */
data class SyncWakeEstimate(val workdays: String?, val offDays: String?)

/**
 * One of the user's Beeminder goals (ADR 43), as a PC read it: derived only, never the token. The phone opens no
 * connection for it.
 *
 * @property needed What's needed to stay on track, in Beeminder's words (e.g. "+2 within 1 day"); may be empty.
 * @property safeDays Days of buffer when the PC last asked; 0 or less = due by the next deadline.
 * @property dueAt When the goal derails without a data point.
 */
data class SyncBeeminderGoal(val title: String, val needed: String, val safeDays: Int, val dueAt: Instant)

/** A phone's smart bedtime (ADR 34): per kind of night, null for a kind that's off. */
data class SyncSmartBedtime(val workdays: SyncSmartLevel?, val offDays: SyncSmartLevel?)

/**
 * One kind of night's smart bedtime: [levelMinutes] in bed before waking (null while learning), and the status line's
 * numbers ([averageAsleep] as "HH:mm"). Only derived numbers, never sleep sessions or times of phone use.
 */
data class SyncSmartLevel(
    val levelMinutes: Int?,
    val nights: Int,
    val latencyMinutes: Int,
    val averageAsleep: String?,
    val averageSleptMinutes: Int?,
    val madeThisWeek: Int,
    val askedThisWeek: Int,
)

/**
 * A flexible lunch: when it started and ends. Every device takes the newest edit for the day. [editedAt] is when it
 * was last started, made longer or shorter, or ended early; null from a device that doesn't send it ([start] stands for it).
 */
data class SyncLunch(val start: java.time.Instant, val end: java.time.Instant, val editedAt: java.time.Instant? = null) {
    /** What two devices' lunches are compared by: the newer one wins. */
    val lastEdit: java.time.Instant get() = editedAt ?: start
}

/**
 * Something the user did on the phone for a PC (ADR 26): tick a task, park a thought, tick a come-back, step away or
 * come back. [target] is that PC's sync device id; [item] the task, subtask or come-back; [text] the thought to park.
 */
data class SyncAction(
    val id: String,
    val kind: String,
    val target: java.util.UUID,
    val item: String? = null,
    val text: String? = null,
    val at: java.time.Instant,
) {
    companion object {
        const val TICK_TASK = "tickTask"
        const val PARK = "park"
        const val TICK_COMEBACK = "tickComeback"
        const val STEP_AWAY = "stepAway"
        const val BACK = "back"
    }
}

/** A PC's running session: the profile, the current phase's start and end (null: no set end), a break, stepped away since. */
data class SyncRunning(
    val profile: String?,
    val phaseStartedAt: java.time.Instant,
    val phaseEndsAt: java.time.Instant?,
    val isBreak: Boolean,
    val awaySince: java.time.Instant?,
)
