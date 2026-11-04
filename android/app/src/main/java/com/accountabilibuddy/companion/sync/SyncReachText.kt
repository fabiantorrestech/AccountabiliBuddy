package com.accountabilibuddy.companion.sync

import java.time.Instant
import java.time.ZoneId
import java.time.format.DateTimeFormatter
import java.util.Locale

/**
 * The plain words for "who can this phone actually reach": the line on Devices & sync while no other device has been heard
 * from, and what Sync now reports. A code gives the group's key and no way to reach anyone, so the words say what is missing
 * instead of claiming success. The PC's twin (App's SyncReachText.cs) uses the same wording with "PC" for "phone". Only the
 * three ways that move files by themselves count ([SyncMethod]); QR codes and sync files are by hand. No Android here.
 */
object SyncReachText {
    /** Linked, no way that moves files by itself is on. */
    const val NO_METHOD_LINE =
        "Linked, but this phone can't reach your other devices yet. Turn on the sync method they use: a sync folder, Google Drive or your relay."

    /** What Sync now says when no such way is on. */
    const val NOTHING_SENT_LINE = "No sync method is on, so nothing was sent. Turn on a sync folder, Google Drive or your relay."

    /** A way is on, nobody heard yet. */
    fun nothingYetLine(on: List<SyncMethod>): String =
        "Nothing has come through ${names(on)} yet. Your other devices must use the same method."

    /** The ways that are on and have what they need, in the order sync folder, Google Drive, own relay. */
    fun methodsOn(settings: PhoneSyncSettings): List<SyncMethod> =
        listOf(SyncMethod.Folder, SyncMethod.Drive, SyncMethod.Relay).filter { it.isLive(settings) }

    /** The line for Devices & sync while no other device has been heard from. */
    fun whileNobodyHeard(settings: PhoneSyncSettings): String {
        val on = methodsOn(settings)
        return if (on.isEmpty()) NO_METHOD_LINE else nothingYetLine(on)
    }

    /**
     * What Sync now reports after reading and writing: who was reached, or that nobody was and why.
     *
     * @param others The other devices this phone holds (not the removed ones).
     * @param failed The ways that are on but reported a problem on this pass.
     */
    fun syncNowLine(settings: PhoneSyncSettings, others: List<ReceivedDevice>, failed: Set<SyncMethod>, now: Instant, zone: ZoneId): String {
        val on = methodsOn(settings)
        if (on.isEmpty()) return NOTHING_SENT_LINE

        val sent = on.filterNot { it in failed }
        val failure = if (failed.isEmpty()) "" else "Couldn't send through ${names(on.filter { it in failed })}. "
        if (sent.isEmpty()) return failure + "Nothing was sent."

        // Heard through a way that is on; a device that last came by QR or a file is not "reached" by Sync now.
        val onCards = on.map { it.cardMethod }
        val heard = others.filter { it.method in onCards }.sortedByDescending { it.file.device.writtenAt }
        val sentPart = "Sent through ${names(sent)}. "
        if (heard.isEmpty()) return failure + sentPart + "No other device has answered yet. They must use the same method."

        return failure + sentPart + "Heard from " +
            joinAnd(heard.map { "${it.file.device.name} (sent ${whenText(it.file.device.writtenAt, now, zone)})" }) + "."
    }

    /** "10:35 AM" today, "yesterday 10:35 AM", else "Mon 10:35 AM"; 12-hour whatever the system says. */
    fun whenText(at: Instant, now: Instant, zone: ZoneId): String {
        val local = at.atZone(zone)
        val today = now.atZone(zone).toLocalDate()
        val time = CLOCK.format(local)
        return when (local.toLocalDate()) {
            today -> time
            today.minusDays(1) -> "yesterday $time"
            else -> "${DAY.format(local)} $time"
        }
    }

    /** "4:05 PM": a time of day in 12-hour, whatever the system says. */
    fun clock(at: Instant, zone: ZoneId): String = CLOCK.format(at.atZone(zone))

    private fun names(methods: List<SyncMethod>): String = joinAnd(methods.map(::nameOf))

    private fun nameOf(method: SyncMethod): String = when (method) {
        SyncMethod.Folder -> "the sync folder"
        SyncMethod.Drive -> "Google Drive"
        SyncMethod.Relay -> "your relay"
    }

    private fun joinAnd(items: List<String>): String =
        if (items.size <= 1) items.joinToString("") else items.dropLast(1).joinToString(", ") + " and " + items.last()

    private val CLOCK: DateTimeFormatter = DateTimeFormatter.ofPattern("h:mm a", Locale.US)
    private val DAY: DateTimeFormatter = DateTimeFormatter.ofPattern("EEE", Locale.US)
}
