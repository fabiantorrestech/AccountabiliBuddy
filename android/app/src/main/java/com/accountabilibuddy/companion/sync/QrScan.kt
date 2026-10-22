package com.accountabilibuddy.companion.sync

import com.accountabilibuddy.shared.sync.SyncQr
import com.accountabilibuddy.shared.sync.SyncQrCollector

/** What one scanned link did, with the words the phone shows for it (null for a whole file: the caller says more). */
sealed class QrScanStep(val text: String?) {
    class Progress(val received: Int, val total: Int) : QrScanStep("$received of $total scanned")

    class Complete(val sealed: ByteArray) : QrScanStep(null)

    object OtherGroup : QrScanStep("This code is from another device group")

    object NotACode : QrScanStep("That isn't a sync code.")

    object Unreadable : QrScanStep("Those codes didn't fit together. Scan them again.")
}

/**
 * The PC's QR codes for one sync file, scanned one by one in any order ([SyncQrCollector]). A code from another
 * device group is ignored before it reaches the collector, so it never drops the codes already scanned.
 */
class QrScan {
    private val collector = SyncQrCollector()

    /** Takes in one scanned link, checked against this phone's group id (8 lower-case hex characters). */
    fun add(link: String, groupId: String): QrScanStep {
        if (!link.startsWith(SyncQr.LINK_START, ignoreCase = true)) {
            return QrScanStep.NotACode
        }

        if (groupOf(link) != groupId) {
            return QrScanStep.OtherGroup
        }

        if (!collector.add(link)) {
            return QrScanStep.Unreadable
        }

        val whole = collector.complete
        if (whole != null) {
            collector.reset()
            return QrScanStep.Complete(whole)
        }

        return QrScanStep.Progress(collector.received, collector.total)
    }

    /** Forgets the codes scanned so far. */
    fun reset() = collector.reset()

    // The link's g (group id); the collector checks the rest.
    private fun groupOf(link: String): String? =
        link.substring(SyncQr.LINK_START.length).split('&')
            .firstOrNull { it.startsWith("g=") }
            ?.substring(2)
}
