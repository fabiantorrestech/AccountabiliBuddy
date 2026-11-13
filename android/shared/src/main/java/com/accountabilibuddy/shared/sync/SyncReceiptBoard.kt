package com.accountabilibuddy.shared.sync

import java.time.Duration
import java.time.OffsetDateTime

/** The colour of one dot on the sync troubleshooting board (ADR 86). Never the only signal: the UI pairs it with an icon and words. */
enum class SyncDotColor { GREEN, YELLOW, RED, GREY }

/** Why a dot has its colour (the PC's `SyncDotReason`; the UI picks the words). */
enum class SyncDotReason {
    CONFIRMED, CAUGHT_UP, WAITING, NO_FILE_YET, OTHER_METHOD, OLDER_APP, HELD, NOT_CONFIRMED, FAILED, REFUSED, METHOD_QUIET, NO_REPORT,
    NOT_TAKEN, WATCH, REMOVED, SILENT, NOT_SENT, METHOD_NOT_USED, NO_OTHER_DEVICES,
}

/** One dot: a colour and why. */
data class SyncDot(val color: SyncDotColor, val reason: SyncDotReason)

/**
 * What the sender knows about its own newest file: its [newest] `writtenAt`, per method when it [reachedAt] the method, and per method the
 * newest `writtenAt` of its files that had reached the method by the time of the receiver's latest file ([previousSentAt]).
 */
data class SyncSentFacts(
    val newest: OffsetDateTime,
    val reachedAt: Map<String, OffsetDateTime>,
    val previousSentAt: Map<String, OffsetDateTime>? = null,
)

/**
 * What the sender knows about one other device: its card [kind], whether it is [removed], the newest `writtenAt` held from it ([writtenAt]),
 * whether its latest file carries a `receipts` list ([writesReceipts]), its card's `seen` value for this device ([seen]), its [receipt] for this
 * device, and per method the newest `writtenAt` of its files read by that method ([lastViaMethod]).
 */
data class SyncReceiverFacts(
    val kind: String,
    val removed: Boolean,
    val writtenAt: OffsetDateTime?,
    val writesReceipts: Boolean,
    val seen: OffsetDateTime?,
    val receipt: SyncReceipt?,
    val lastViaMethod: Map<String, OffsetDateTime>? = null,
)

/**
 * The pure colour rule of the sync troubleshooting board (ADR 86; the PC's `SyncReceiptBoard`, same cases in the fixtures of the area sync1a),
 * judged against the receiver's OWN latest file: a receiver that simply has not written since my file reached the method is yellow, never red.
 * Red means the receiver wrote after and did not confirm, or reported a failure. Removed devices, watches and long-silent devices are never red,
 * and a device alone in its group has nothing to judge.
 */
object SyncReceiptBoard {
    /** A receiver's clock and the write itself may differ this much before "wrote after" holds. */
    val SKEW: Duration = Duration.ofMinutes(2)

    /** A device that sent no file for this long is "not seen since", grey. */
    val SILENT: Duration = Duration.ofDays(30)

    /** A method is quiet after this many expected intervals. */
    const val QUIET_INTERVALS = 3

    /**
     * The dot of one sync method for one receiver; [receiver] null = no other device in the group. A yellow dot turns red when files stopped
     * arriving by this method while the receiver writes elsewhere. [expectedInterval]: how often a receiver is expected to write when this method
     * delivers by itself, the caller's real setting for the method (null for a method used by hand, QR and file: never quiet).
     */
    fun method(method: String, now: OffsetDateTime, sent: SyncSentFacts, receiver: SyncReceiverFacts?, expectedInterval: Duration?): SyncDot {
        val dot = judge(method, now, sent, receiver)
        return if (dot.color == SyncDotColor.YELLOW) quiet(method, now, sent, receiver, expectedInterval) ?: dot else dot
    }

    private fun judge(method: String, now: OffsetDateTime, sent: SyncSentFacts, receiver: SyncReceiverFacts?): SyncDot {
        if (receiver == null) return SyncDot(SyncDotColor.GREY, SyncDotReason.NO_OTHER_DEVICES)
        neverRed(now, receiver)?.let { return it }
        val reachedAt = sent.reachedAt[method] ?: return SyncDot(SyncDotColor.GREY, SyncDotReason.NOT_SENT)
        val receipt = if (receiver.writesReceipts) receiver.receipt else null
        val byMethod = receipt?.byMethod?.get(method)
        if (byMethod != null && !before(byMethod, sent.newest)) return SyncDot(SyncDotColor.GREEN, SyncDotReason.CONFIRMED)
        // A failure counts when it happened after my newest file reached the method (an old one-off, before that, is old news).
        if (receipt?.failed?.containsKey(method) == true) {
            val failedAt = receipt.failedAt?.get(method)
            if (failedAt == null || !before(failedAt.plus(SKEW), reachedAt)) return SyncDot(SyncDotColor.RED, SyncDotReason.FAILED)
        }
        val receiverWrote = receiver.writtenAt ?: return SyncDot(SyncDotColor.YELLOW, SyncDotReason.NO_FILE_YET)

        // It confirms my files by other methods only and has no record of this one, either way: it does not use this method. Never red.
        val used = receipt?.byMethod
        if (used != null && used.isNotEmpty() && !used.containsKey(method) && receiver.lastViaMethod?.containsKey(method) != true) {
            return SyncDot(SyncDotColor.GREY, SyncDotReason.METHOD_NOT_USED)
        }

        // Caught up as of its last file: it confirmed everything I had put on this method by then, and my newest file is newer than that.
        val previous = sent.previousSentAt?.get(method)
        if (byMethod != null && previous != null && !before(byMethod, previous) && before(receiverWrote, reachedAt.plus(SKEW))) {
            return SyncDot(SyncDotColor.GREEN, SyncDotReason.CAUGHT_UP)
        }

        val held = if (receipt != null) receipt.held else receiver.seen ?: DotNetTime.MIN
        val confirmsAny = !before(held, sent.newest)
        if (!receiver.writesReceipts) {
            // An older app (or a QR file): all it says is what it holds.
            return if (confirmsAny || before(receiverWrote, reachedAt.plus(SKEW))) SyncDot(SyncDotColor.YELLOW, SyncDotReason.OLDER_APP)
            else SyncDot(SyncDotColor.RED, SyncDotReason.NOT_CONFIRMED)
        }

        if (confirmsAny) return SyncDot(SyncDotColor.YELLOW, SyncDotReason.OTHER_METHOD)
        if (before(receiverWrote, reachedAt.plus(SKEW))) return SyncDot(SyncDotColor.YELLOW, SyncDotReason.WAITING)
        return SyncDot(SyncDotColor.RED, SyncDotReason.NOT_CONFIRMED)
    }

    // Only when this device has read a file from the receiver by this method at least once (no record: nothing "stopped").
    private fun quiet(method: String, now: OffsetDateTime, sent: SyncSentFacts, receiver: SyncReceiverFacts?, expectedInterval: Duration?): SyncDot? {
        if (receiver == null || neverRed(now, receiver) != null) return null
        val interval = expectedInterval ?: return null
        if (!sent.reachedAt.containsKey(method)) return null
        val receiverWrote = receiver.writtenAt ?: return null
        val reference = receiver.lastViaMethod?.get(method) ?: return null
        return if (Duration.between(reference, receiverWrote) > interval.multipliedBy(QUIET_INTERVALS.toLong())) SyncDot(SyncDotColor.RED, SyncDotReason.METHOD_QUIET) else null
    }

    /** The dot of one part for one receiver, from the receiver's receipt for this device; [reachedAnyAt]: when my newest file first reached any method (null: nowhere). */
    fun part(code: String, now: OffsetDateTime, sent: SyncSentFacts, reachedAnyAt: OffsetDateTime?, receiver: SyncReceiverFacts?): SyncDot {
        if (receiver == null) return SyncDot(SyncDotColor.GREY, SyncDotReason.NO_OTHER_DEVICES)
        neverRed(now, receiver)?.let { return it }
        if (!receiver.writesReceipts) return SyncDot(SyncDotColor.YELLOW, SyncDotReason.OLDER_APP)
        val part = receiver.receipt?.parts?.get(code) ?: return SyncDot(SyncDotColor.YELLOW, SyncDotReason.NO_REPORT)
        val current = !before(part.at, sent.newest)
        when {
            part.state == SyncReceiptParts.OFF || part.state == SyncReceiptParts.ABSENT -> return SyncDot(SyncDotColor.GREY, SyncDotReason.NOT_TAKEN)
            part.state == SyncReceiptParts.REFUSED && current -> return SyncDot(SyncDotColor.RED, SyncDotReason.REFUSED)
            part.state == SyncReceiptParts.HELD && current -> return SyncDot(SyncDotColor.YELLOW, SyncDotReason.HELD)
            part.state == SyncReceiptParts.TAKEN && current -> return SyncDot(SyncDotColor.GREEN, SyncDotReason.CONFIRMED)
        }

        // It judged an older file of mine: wait for its next file, unless it wrote after my newest file went out.
        val receiverWrote = receiver.writtenAt
        if (reachedAnyAt == null || receiverWrote == null || before(receiverWrote, reachedAnyAt.plus(SKEW))) return SyncDot(SyncDotColor.YELLOW, SyncDotReason.WAITING)
        return SyncDot(SyncDotColor.RED, SyncDotReason.NOT_CONFIRMED)
    }

    // Removed, watch and silent devices are grey, whatever else is true.
    private fun neverRed(now: OffsetDateTime, receiver: SyncReceiverFacts): SyncDot? {
        if (receiver.removed) return SyncDot(SyncDotColor.GREY, SyncDotReason.REMOVED)
        if (receiver.kind == "watch") return SyncDot(SyncDotColor.GREY, SyncDotReason.WATCH)
        val wrote = receiver.writtenAt
        if (wrote != null && Duration.between(wrote, now) > SILENT) return SyncDot(SyncDotColor.GREY, SyncDotReason.SILENT)
        return null
    }

    private fun before(first: OffsetDateTime, second: OffsetDateTime): Boolean = DotNetTime.compare(first, second) < 0
}
