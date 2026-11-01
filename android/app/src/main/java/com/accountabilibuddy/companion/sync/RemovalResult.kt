package com.accountabilibuddy.companion.sync

import java.util.UUID

/**
 * The outcome of [SyncCoordinator.removeDevices] (ADR 54).
 *
 * @property done True when the rekey was written and this phone switched to the new key.
 * @property problem In plain words why not, when [done] is false; nothing changed then.
 * @property notWrapped Remaining devices that had no public key and so can't receive the new key: they are cut off like the removed ones.
 * @property failedMethods How many sync methods failed to take the rekey file (others did): devices that sync only through those won't hear of it.
 */
data class RemovalResult(val done: Boolean, val problem: String?, val notWrapped: List<UUID>, val failedMethods: Int) {
    companion object {
        fun failed(problem: String) = RemovalResult(false, problem, emptyList(), 0)
    }
}
