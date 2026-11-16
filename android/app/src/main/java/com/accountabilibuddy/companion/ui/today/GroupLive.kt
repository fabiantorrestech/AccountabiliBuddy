package com.accountabilibuddy.companion.ui.today

import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import com.accountabilibuddy.companion.guidance.TrustedTime
import com.accountabilibuddy.companion.sync.PcFileControl
import com.accountabilibuddy.companion.sync.ReceivedDevice
import com.accountabilibuddy.shared.sync.GroupSessionLive
import com.accountabilibuddy.shared.sync.GroupSessionStale
import com.accountabilibuddy.shared.sync.GroupSessions
import com.accountabilibuddy.shared.sync.ReceivedSyncFile
import kotlinx.coroutines.delay
import java.time.Instant

/** What the group's files say about a session on another device at [now] (trusted time): the live one (R2) and a quiet phone one (R3). */
class GroupLive(val live: GroupSessionLive?, val stale: GroupSessionStale?, val now: Instant)

/**
 * The live session on another device and the quiet one, read from the files this phone holds ([received]: the devices still in the group, never this phone's
 * own file). Looked at again with the files and every 15 seconds, so "Running on Pixel · 32 min left" stays current without a clock per second. The PC rule is
 * [PcFileControl.groupLive], the same one the PC card and the lock seam read, so Today and Start always agree (PS1b review S1).
 */
@Composable
fun rememberGroupLive(received: List<ReceivedDevice>): GroupLive {
    var tick by remember { mutableIntStateOf(0) }
    LaunchedEffect(Unit) {
        while (true) {
            delay(15_000)
            tick++
        }
    }
    return remember(received, tick) {
        val now = TrustedTime.now()
        val others = received.map { ReceivedSyncFile(it.file, it.receivedAt) }
        GroupLive(
            live = GroupSessions.liveElsewhere(others, now) { PcFileControl.groupLive(it, now) },
            stale = GroupSessions.staleElsewhere(others, now),
            now = now,
        )
    }
}
