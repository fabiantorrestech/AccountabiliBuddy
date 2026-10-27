package com.accountabilibuddy.watch.link

import java.util.concurrent.Executors

/**
 * One background thread that runs its jobs in the order they were queued. The stores use it for what a tap or a message
 * triggers besides the in-memory state (files, prefs, alarms, notifications, Tile and complication requests), so the
 * main thread never waits on disk or on the system.
 */
class SerialBackground(name: String) {
    private val executor = Executors.newSingleThreadExecutor { runnable ->
        Thread(runnable, name).apply { isDaemon = true }
    }

    /** Queues [job]; returns at once. */
    fun run(job: () -> Unit) {
        executor.execute {
            // One failed job (a revoked permission, a dead service) must not stop the jobs after it.
            runCatching(job)
        }
    }

    /** Queues [job] and waits for it (and the jobs before it): for callers that are already off the main thread. */
    fun runAndWait(job: () -> Unit) {
        runCatching { executor.submit { runCatching(job) }.get() }
    }
}
