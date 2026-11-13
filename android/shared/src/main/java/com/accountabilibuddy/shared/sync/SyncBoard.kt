package com.accountabilibuddy.shared.sync

import java.time.Duration
import java.time.OffsetDateTime
import java.time.ZoneId
import java.time.ZoneOffset
import java.time.format.DateTimeFormatter
import java.util.Locale
import java.util.UUID

/** One item (a part of the sync file) under a device on the confirmation board: its [code] ([SyncReceiptParts.CODES]) and its [dot]. */
data class SyncBoardPart(val code: String, val dot: SyncDot)

/**
 * One other device under a method (SYNC1c; the PC's `SyncBoardDevice`). [name] is on screen only: the log and the report use [ordinal]
 * ("Phone 1"). [confirmedFile] is the time of this phone's file it last confirmed by this method; [lastFileAt] when its newest file was written.
 */
data class SyncBoardDevice(
    val id: UUID,
    val name: String,
    val ordinal: String,
    val kind: String,
    val appVersion: String,
    val dot: SyncDot,
    val confirmedFile: OffsetDateTime?,
    val lastFileAt: OffsetDateTime?,
    val parts: List<SyncBoardPart>,
)

/** One sync method in use, with a row per other device (empty while this phone is alone in the group). */
data class SyncBoardMethod(val method: String, val title: String, val devices: List<SyncBoardDevice>)

/** The per-method, per-device, per-item confirmation board (SYNC1c, ADR 86). [newest]: when this phone's newest file was put anywhere. */
data class SyncBoard(val methods: List<SyncBoardMethod>, val newest: OffsetDateTime?) {
    /** No method is on: nothing to confirm, no warning. */
    val notSyncing: Boolean get() = methods.isEmpty()

    /** Methods are on but no other device has been heard from. */
    val noOtherDevices: Boolean get() = methods.isNotEmpty() && methods.all { it.devices.isEmpty() }
}

/**
 * Builds the board from what this phone knows (the PC's `SyncBoardBuilder`): the files it holds from the other devices, the receipts in them,
 * its receiver-local record and its send log. The colours are the pure rule [SyncReceiptBoard]. No transport and no clock of its own.
 */
object SyncBoardBuilder {
    /** The method order on the board. */
    val ORDER: List<String> = listOf(SyncReceiptParts.FOLDER, SyncReceiptParts.DRIVE, SyncReceiptParts.RELAY, SyncReceiptParts.QR, SyncReceiptParts.FILE)

    /** The items shown under a device, in this order (older task lists and phone actions are internal). */
    val SHOWN_PARTS: List<String> = listOf("tasks", "goals", "goalHistory", "sessions", "dayPlan", "settings", "steps", "lunch")

    /**
     * @param ownMethods The methods this phone has on (its card's list).
     * @param files The files held from the devices still in the group (never a removed device's).
     * @param interval A method's real expected write interval (null: used by hand, never quiet).
     */
    fun build(
        now: OffsetDateTime,
        self: UUID,
        ownMethods: Collection<String>,
        files: List<SyncFile>,
        local: SyncReceiptsLocal,
        sent: SyncSendLog,
        interval: (String) -> Duration?,
    ): SyncBoard {
        val ordinals = ordinals(files.map { it.device })
        val methods = ArrayList<SyncBoardMethod>()
        for (method in ORDER.filter { it in ownMethods }) {
            val rows = files.sortedBy { ordinals[it.device.deviceId] }.map { row(now, self, method, it, ordinals, local, sent, interval) }
            methods.add(SyncBoardMethod(method, titleOf(method), rows))
        }

        return SyncBoard(methods, sent.newest)
    }

    /** "PC 1", "Phone 1", "Watch 1": the stable names the log and the report use (kind, then device id order). */
    fun ordinals(cards: List<SyncDeviceCard>): Map<UUID, String> {
        val result = HashMap<UUID, String>()
        for ((kind, group) in cards.groupBy { it.kind }) {
            group.sortedBy { it.deviceId.toString() }.forEachIndexed { index, card -> result[card.deviceId] = "${kindLabel(kind)} ${index + 1}" }
        }

        return result
    }

    /** The method's name on screen. */
    fun titleOf(method: String): String = when (method) {
        SyncReceiptParts.FOLDER -> "Shared folder"
        SyncReceiptParts.DRIVE -> "Google Drive"
        SyncReceiptParts.RELAY -> "Own relay"
        SyncReceiptParts.QR -> "QR codes"
        else -> "File import"
    }

    private fun kindLabel(kind: String): String = when (kind) {
        SyncDeviceCard.KIND_PC -> "PC"
        SyncDeviceCard.KIND_PHONE -> "Phone"
        SyncDeviceCard.KIND_WATCH -> "Watch"
        else -> "Device"
    }

    private fun row(
        now: OffsetDateTime,
        self: UUID,
        method: String,
        file: SyncFile,
        ordinals: Map<UUID, String>,
        local: SyncReceiptsLocal,
        sent: SyncSendLog,
        interval: (String) -> Duration?,
    ): SyncBoardDevice {
        val card = file.device
        val mine = file.receipts?.firstOrNull { it.from == self }
        val theirs = local.senders.firstOrNull { it.from == card.deviceId }
        val seen = card.seen[self]?.atOffset(ZoneOffset.UTC)
        val wrote = card.writtenAt.atOffset(ZoneOffset.UTC)
        val facts = SyncReceiverFacts(card.kind, false, wrote, file.receipts != null, seen, mine, theirs?.byMethod)
        val sentFacts = sent.facts(wrote) ?: SyncSentFacts(DotNetTime.MIN, emptyMap())
        val dot = SyncReceiptBoard.method(method, now, sentFacts, facts, interval(method))
        val reachedAny = sentFacts.reachedAt.values.minWithOrNull { a, b -> DotNetTime.compare(a, b) }
        val parts = SHOWN_PARTS.map { code -> SyncBoardPart(code, SyncReceiptBoard.part(code, now, sentFacts, reachedAny, facts)) }
        val confirmed = mine?.byMethod?.get(method)
        return SyncBoardDevice(card.deviceId, card.name, ordinals.getValue(card.deviceId), card.kind, card.appVersion, dot, confirmed, wrote, parts)
    }
}

/**
 * The words for a board dot (SYNC1c): colour is never the only signal, so each colour has its icon (the UI maps [SyncDotColor] to Material
 * icons CheckCircle, ClockOutline, AlertCircle and MinusCircleOutline, the same as the PC) and every reason has a plain sentence. Times are
 * 12-hour with AM/PM.
 */
object SyncBoardWords {
    /** The colour's own word, for the accessible name. */
    fun colorWord(color: SyncDotColor): String = when (color) {
        SyncDotColor.GREEN -> "Received"
        SyncDotColor.YELLOW -> "Waiting"
        SyncDotColor.RED -> "Problem"
        SyncDotColor.GREY -> "Not applicable"
    }

    /** The sentence for a dot's reason. */
    fun words(dot: SyncDot): String = when (dot.reason) {
        SyncDotReason.CONFIRMED -> "Received your newest file"
        SyncDotReason.CAUGHT_UP -> "Up to date as of its last file"
        SyncDotReason.WAITING -> "Waiting for its next file to confirm"
        SyncDotReason.NO_FILE_YET -> "No file from it yet"
        SyncDotReason.OTHER_METHOD -> "Confirmed, but through another way"
        SyncDotReason.OLDER_APP -> "Older app: it says what it holds, not how or which parts"
        SyncDotReason.HELD -> "Held back: a session, a wait or a question"
        SyncDotReason.NOT_CONFIRMED -> "Wrote since, but didn't confirm your newest file"
        SyncDotReason.FAILED -> "Couldn't read your file"
        SyncDotReason.REFUSED -> "Refused your file"
        SyncDotReason.METHOD_QUIET -> "Nothing has come from it by this way for a while"
        SyncDotReason.NO_REPORT -> "No report on this yet"
        SyncDotReason.NOT_TAKEN -> "Not taken: switched off there or not in this file"
        SyncDotReason.WATCH -> "A watch: reached through its phone"
        SyncDotReason.REMOVED -> "Removed from the group"
        SyncDotReason.SILENT -> "Not heard from for over 30 days"
        SyncDotReason.NOT_SENT -> "Your newest file isn't on this way yet"
        SyncDotReason.METHOD_NOT_USED -> "Not used by that device"
        SyncDotReason.NO_OTHER_DEVICES -> "No other devices yet"
    }

    /** The name of a part code. */
    fun partName(code: String): String = when (code) {
        "tasks" -> "Tasks, labels and views"
        "goals" -> "Goals"
        "goalHistory" -> "Goal history"
        "sessions" -> "Sessions"
        "dayPlan" -> "Day plan"
        "settings" -> "Settings"
        "steps" -> "Steps"
        "lunch" -> "Lunch"
        "oldLists" -> "Older task lists"
        "actions" -> "Phone actions"
        else -> "Other"
    }

    /** The sentence for a failure or leftover code of an unreadable file. */
    fun reasonOfFile(reason: String): String = when (reason) {
        "damaged" -> "Damaged, or cut off"
        "tooBig" -> "Too big to be a sync file"
        "newerFormat" -> "From a newer version of the app"
        SyncUnreadableFile.OTHER_GROUP -> "Another group's leftover (from before a device was removed)"
        SyncUnreadableFile.OWN_COPY -> "An extra copy of this phone's own file"
        else -> "Can't be read"
    }

    private val CLOCK: DateTimeFormatter = DateTimeFormatter.ofPattern("h:mm a", Locale.US)
    private val DAY: DateTimeFormatter = DateTimeFormatter.ofPattern("MMM d", Locale.US)

    /** 12-hour time of day ("4:05 PM"); a day before today adds the date ("Nov 12, 4:05 PM"). */
    fun clock(time: OffsetDateTime, now: OffsetDateTime, zone: ZoneId): String {
        val local = time.atZoneSameInstant(zone)
        val today = now.atZoneSameInstant(zone)
        val clock = CLOCK.format(local)
        return if (local.toLocalDate() == today.toLocalDate()) clock else DAY.format(local) + ", " + clock
    }
}
