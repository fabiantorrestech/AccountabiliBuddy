package com.accountabilibuddy.companion.session

/**
 * Waits for work for at most a time, whatever the work does (PS2b verify V-S1). The heartbeat receiver stays open (`goAsync`) only as long as this lets it: the
 * work runs on its own thread, and the wait is a timed `join`, so a blocking socket or file call (which a coroutine's cancellation can't interrupt) can't hold the
 * receiver past [timeoutMs]. The work is not stopped: it goes on after the receiver has finished. Pure JVM, so the checks drive it with a pass that blocks.
 */
object PhoneSessionBoundedWait {
    /** True when [work] finished within [timeoutMs]; false when it was still running (it then goes on by itself). */
    fun run(timeoutMs: Long, work: () -> Unit): Boolean {
        val thread = Thread({ runCatching(work) }, "own-file-passes").apply { isDaemon = true }
        thread.start()
        thread.join(timeoutMs)
        return !thread.isAlive
    }
}
