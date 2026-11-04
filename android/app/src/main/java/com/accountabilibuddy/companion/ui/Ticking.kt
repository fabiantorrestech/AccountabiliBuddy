package com.accountabilibuddy.companion.ui

import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.State
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberUpdatedState
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.compose.LocalLifecycleOwner
import androidx.lifecycle.repeatOnLifecycle
import kotlinx.coroutines.delay
import java.time.Instant

/**
 * "Now", looked at again only while the app is in front (and at once when it comes back). [periodMs] says how long to wait
 * after each look, given the time just read; null stops looking until [key] changes or the app comes back to the front.
 * A card counts down at 1 s only while something is counting, and rests (or stops) otherwise.
 *
 * Display only: the rules that act on time (waits, locks) read the clock themselves and run in the services.
 */
@Composable
fun rememberTickingNow(key: Any?, periodMs: (Instant) -> Long?): State<Instant> {
    val now = remember { mutableStateOf(Instant.now()) }
    val period = rememberUpdatedState(periodMs)
    val lifecycle = LocalLifecycleOwner.current.lifecycle
    LaunchedEffect(lifecycle, key) {
        lifecycle.repeatOnLifecycle(Lifecycle.State.STARTED) {
            while (true) {
                val read = Instant.now()
                now.value = read
                delay(period.value(read) ?: break)
            }
        }
    }
    return now
}

/**
 * Runs [block] now and then every [periodMs] while the app is in front (and at once when it comes back), for a page that shows
 * something stored in preferences. [periodMs] is read after each run, so a page can look often while something is pending
 * and rarely otherwise.
 */
@Composable
fun PollWhileVisible(periodMs: () -> Long, block: () -> Unit) {
    val period = rememberUpdatedState(periodMs)
    val run = rememberUpdatedState(block)
    val lifecycle = LocalLifecycleOwner.current.lifecycle
    LaunchedEffect(lifecycle) {
        lifecycle.repeatOnLifecycle(Lifecycle.State.STARTED) {
            while (true) {
                run.value()
                delay(period.value())
            }
        }
    }
}
