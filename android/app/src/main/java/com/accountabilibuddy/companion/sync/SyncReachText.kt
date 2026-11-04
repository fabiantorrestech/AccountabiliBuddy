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
    /** No way that moves files by itself is on. */
    const val NO_METHOD_LINE =
        "This phone can't reach your other devices yet. Turn on the sync method they use: a sync folder, Google Drive or your relay."

    /** What Sync now says when no such way is on. */
    const val NOTHING_SENT_LINE = "No sync method is on, so nothing was sent. Turn on a sync folder, Google Drive or your relay."

    /**
     * What a Sync now says, twice: [short] fits a toast (Android shows two lines of a text toast) and [full] goes on Devices & sync.
     * [settings] is what the phone had when it ran, so the page can drop the full line once the settings change.
     */
    class Report(val short: String, val full: String, val settings: PhoneSyncSettings? = null)

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
     * The ways that are on but did not finish a pass that began at [started]: a way whose last good pass is older (a damaged
     * file read still counts as a pass; a write that failed, or a way that could not be reached, does not).
     *
     * @param lastSyncAt Each way's last good pass ([FolderStatus.lastSyncAt]).
     */
    fun failedSends(settings: PhoneSyncSettings, lastSyncAt: Map<SyncMethod, Instant?>, started: Instant): Set<SyncMethod> =
        methodsOn(settings).filter { lastSyncAt[it]?.isBefore(started) ?: true }.toSet()

    /**
     * What Sync now reports after reading and writing: who was reached, or that nobody was and why.
     *
     * @param others The other devices this phone holds (not the removed ones).
     * @param failed The ways whose pass failed; only the ones that are on count.
     * @param passStart When this Sync now began: a device whose copy changed since is "heard from", the others "last heard from".
     */
    fun syncNowLine(
        settings: PhoneSyncSettings, others: List<ReceivedDevice>, failed: Set<SyncMethod>, passStart: Instant, now: Instant, zone: ZoneId,
    ): String {
        val on = methodsOn(settings)
        if (on.isEmpty()) return NOTHING_SENT_LINE

        val sent = on.filterNot { it in failed }
        val failedOn = on.filter { it in failed }
        val failure = if (failedOn.isEmpty()) "" else "Couldn't send through ${names(failedOn)}. "
        if (sent.isEmpty()) return failure + "Nothing was sent."

        val heard = heardThrough(on, others)
        val sentPart = "Sent through ${names(sent)}. "
        if (heard.isEmpty()) return failure + sentPart + "No other device has answered yet. They must use the same method."

        fun describe(device: ReceivedDevice) = "${device.file.device.name} (sent ${whenText(device.file.device.writtenAt, now, zone)})"
        val fresh = heard.filter { !it.receivedAt.isBefore(passStart) }.map(::describe)
        val older = heard.filter { it.receivedAt.isBefore(passStart) }.map(::describe)
        val reached = if (fresh.isEmpty()) "" else "Heard from ${joinAnd(fresh)}. "
        val last = if (older.isEmpty()) "" else "Last heard from ${joinAnd(older)}."
        return (failure + sentPart + reached + last).trimEnd()
    }

    /** The outcome alone, for a toast: the same facts as [syncNowLine] without the names and times. */
    fun syncNowShort(
        settings: PhoneSyncSettings, others: List<ReceivedDevice>, failed: Set<SyncMethod>, passStart: Instant,
    ): String {
        val on = methodsOn(settings)
        if (on.isEmpty()) return "No sync method is on, so nothing was sent."

        val sent = on.filterNot { it in failed }
        val failedOn = on.filter { it in failed }
        val failure = if (failedOn.isEmpty()) "" else "Couldn't send through ${names(failedOn)}. "
        if (sent.isEmpty()) return failure.trimEnd()

        val heard = heardThrough(on, others)
        val fresh = heard.count { !it.receivedAt.isBefore(passStart) }
        val answer = when {
            heard.isEmpty() -> "No other device has answered yet."
            fresh == 0 -> "No new files from other devices."
            fresh == 1 -> "Heard from 1 device."
            else -> "Heard from $fresh devices."
        }
        return "${failure}Sent through ${names(sent)}. $answer"
    }

    // Heard through a way that is on; a device that last came by QR or a file is not "reached" by Sync now. Newest first.
    private fun heardThrough(on: List<SyncMethod>, others: List<ReceivedDevice>): List<ReceivedDevice> {
        val onCards = on.map { it.cardMethod }
        return others.filter { it.method in onCards }.sortedByDescending { it.file.device.writtenAt }
    }

    /** "10:35 AM" today, "yesterday 10:35 AM", "Mon 10:35 AM" within the week, from 7 days back a date ("Oct 12"); 12-hour whatever the system says. */
    fun whenText(at: Instant, now: Instant, zone: ZoneId): String {
        val local = at.atZone(zone)
        val today = now.atZone(zone).toLocalDate()
        val date = local.toLocalDate()
        val time = CLOCK.format(local)
        return when {
            date == today -> time
            date == today.minusDays(1) -> "yesterday $time"
            !date.isAfter(today.minusDays(7)) -> DATE.format(local)
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
    private val DATE: DateTimeFormatter = DateTimeFormatter.ofPattern("MMM d", Locale.US)
}
