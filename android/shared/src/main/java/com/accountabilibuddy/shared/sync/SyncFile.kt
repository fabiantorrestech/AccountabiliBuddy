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
 */
data class SyncFile(
    val formatVersion: Int,
    val device: SyncDeviceCard,
    val sessions: List<SyncSession>,
    val drift: List<SyncDrift>,
    val dayPlan: SyncDayPlan?,
    val documents: List<SyncDocument>,
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
    }
}

/**
 * A focus session that counts on the device that wrote the file.
 *
 * @property end Null for the session that was running when the file was written ([SyncDeviceCard.writtenAt]).
 * @property profile The profile's name; null for a session added by hand.
 * @property added Added by hand in History (ADR 20); its note never travels.
 */
data class SyncSession(val start: Instant, val end: Instant?, val profile: String?, val added: Boolean)

/**
 * A distracting stretch inside a counted session, from the readable columns only (ADR 8, 17).
 *
 * @property app The app's process name (the package name on the phone).
 * @property site The site's domain, when it was a page in a browser.
 * @property section The site's section (ADR 17), e.g. "r/manybaggers".
 */
data class SyncDrift(val start: Instant, val end: Instant, val app: String, val site: String?, val section: String?)

/**
 * A device's day plan as it travels (ADR 19, 22), with what the clash rule needs: its version, when it was last edited,
 * and the version that edit started from.
 *
 * @property planJson The plan in the PC's day plan shape (the PC's ProfileDocumentSerializer.SerializeDayPlan).
 * @property version The PC's DayPlanVersion of the plan.
 * @property basedOn The version the device held before its last edit; null when it isn't known.
 */
data class SyncDayPlan(val planJson: String, val version: String, val editedAt: Instant, val basedOn: String?)

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
