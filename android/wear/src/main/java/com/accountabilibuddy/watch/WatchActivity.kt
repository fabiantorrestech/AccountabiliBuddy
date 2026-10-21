package com.accountabilibuddy.watch

import android.os.Bundle
import android.os.SystemClock
import android.text.format.DateFormat
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.lifecycle.lifecycleScope
import androidx.wear.compose.foundation.pager.HorizontalPager
import androidx.wear.compose.foundation.pager.rememberPagerState
import androidx.wear.compose.material3.AnimatedPage
import androidx.wear.compose.material3.AppScaffold
import androidx.wear.compose.material3.HorizontalPagerScaffold
import androidx.wear.compose.material3.MaterialTheme
import androidx.wear.compose.material3.ScreenScaffold
import com.accountabilibuddy.shared.SessionView
import com.accountabilibuddy.shared.TodayView
import com.accountabilibuddy.shared.WatchSnapshot
import com.accountabilibuddy.shared.WatchText
import com.accountabilibuddy.shared.WeekView
import com.accountabilibuddy.watch.link.PcLink
import com.accountabilibuddy.watch.link.PhoneLink
import com.accountabilibuddy.watch.link.SnapshotStore
import com.accountabilibuddy.watch.model.SampleData
import com.accountabilibuddy.watch.ui.LinkPage
import com.accountabilibuddy.watch.ui.NotLinkedScreen
import com.accountabilibuddy.watch.ui.SessionPage
import com.accountabilibuddy.watch.ui.TodayPage
import com.accountabilibuddy.watch.ui.WeekPage
import com.accountabilibuddy.watch.ui.use24HourClock
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import java.time.Instant
import java.time.ZoneId

/**
 * The watch app (ADR 21): swipe between today's rings, the running session, the last 7 days and where the data comes
 * from. Shows the last snapshot; asks the phone for news each time it comes up, and when the phone doesn't answer
 * (and hasn't for 2 minutes), asks the PC itself over Wi-Fi if the watch is paired with it and the phone allows it.
 */
class WatchActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        use24HourClock = DateFormat.is24HourFormat(this)
        val snapshots = SnapshotStore.snapshot(this)
        setContent {
            MaterialTheme {
                val stored by snapshots.collectAsState()
                val phoneReachable by PhoneLink.phoneReachable.collectAsState()
                WatchApp(stored, phoneReachable, PcLink.isPaired(this), onAskAgain = { askForNews(force = true) }, onForget = { SnapshotStore.clear(this) })
            }
        }
    }

    override fun onStart() {
        super.onStart()
        askForNews(force = false)
    }

    private fun askForNews(force: Boolean) {
        lifecycleScope.launch {
            val asked = SystemClock.elapsedRealtime()
            val relayAllowed = SnapshotStore.snapshot(this@WatchActivity).value?.relayAllowed ?: true
            if (!PhoneLink.askForNews(this@WatchActivity, relayAllowed, force)) {
                // Asked a moment ago; what came then is still on screen.
                return@launch
            }
            if (PhoneLink.phoneReachable.value == true) {
                delay(PHONE_ANSWER_WAIT_MS)
            }

            val wifiAllowed = SnapshotStore.snapshot(this@WatchActivity).value?.wifiAllowed ?: true
            if (!PhoneLink.heardSince(asked) && PhoneLink.quietFor(PHONE_QUIET_MS) && wifiAllowed && PcLink.isPaired(this@WatchActivity)) {
                PcLink.refresh(this@WatchActivity)
            }
        }
    }

    private companion object {
        // The phone answers within seconds (it may first ask the PC); after that the watch tries the PC itself.
        const val PHONE_ANSWER_WAIT_MS = 12_000L
        const val PHONE_QUIET_MS = 2 * 60_000L
    }
}

@Composable
private fun WatchApp(stored: WatchSnapshot?, phoneReachable: Boolean?, pcPaired: Boolean, onAskAgain: () -> Unit, onForget: () -> Unit) {
    var now by remember { mutableStateOf(Instant.now()) }
    var sample by remember { mutableStateOf<WatchSnapshot?>(null) }
    LaunchedEffect(Unit) {
        while (true) {
            delay(1_000)
            now = Instant.now()
        }
    }

    val zone = ZoneId.systemDefault()
    AppScaffold {
        val snapshot = stored ?: sample
        if (snapshot == null) {
            ScreenScaffold {
                NotLinkedScreen(phoneReachable, onAskAgain, onSample = { sample = SampleData.snapshot(Instant.now(), zone) })
            }
            return@AppScaffold
        }

        val pager = rememberPagerState(pageCount = { PAGES })
        HorizontalPagerScaffold(pagerState = pager) {
            HorizontalPager(state = pager) { page ->
                AnimatedPage(pageIndex = page, pagerState = pager) {
                    ScreenScaffold {
                        when (page) {
                            0 -> TodayPage(TodayView.from(snapshot, now, zone, use24HourClock))
                            1 -> SessionPage(SessionView.from(snapshot.running, now, zone, use24HourClock, dataAt = snapshot.dataAt))
                            2 -> WeekPage(WeekView.from(snapshot, now, zone))
                            else -> LinkPage(
                                pcName = snapshot.pcName,
                                updated = WatchText.updatedAgo(snapshot.dataAt, now),
                                isSample = stored == null,
                                pcPaired = pcPaired,
                                onForget = { if (stored == null) sample = null else onForget() },
                            )
                        }
                    }
                }
            }
        }
    }
}

private const val PAGES = 4
