package com.accountabilibuddy.watch

import android.content.Context
import android.content.Intent
import android.os.Bundle
import android.text.format.DateFormat
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.State
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.derivedStateOf
import androidx.compose.runtime.getValue
import androidx.compose.runtime.key
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.runtime.withFrameMillis
import androidx.compose.ui.platform.LocalContext
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
import com.accountabilibuddy.watch.alerts.PhaseAlertScheduler
import com.accountabilibuddy.watch.link.PhoneLink
import com.accountabilibuddy.watch.link.SnapshotStore
import com.accountabilibuddy.watch.model.SampleData
import com.accountabilibuddy.watch.timers.TimerStore
import com.accountabilibuddy.watch.ui.AlertsPage
import com.accountabilibuddy.watch.ui.LinkPage
import com.accountabilibuddy.watch.ui.NotLinkedScreen
import com.accountabilibuddy.watch.ui.SessionPage
import com.accountabilibuddy.watch.ui.TimersPage
import com.accountabilibuddy.watch.ui.TodayPage
import com.accountabilibuddy.watch.ui.WeekPage
import com.accountabilibuddy.watch.ui.use24HourClock
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import java.time.Instant
import java.time.ZoneId

/**
 * The watch app (ADR 21): swipe between today's rings, the running session, lunch (on a workday), the watch's own
 * timers, the noise player, the last 7 days and where the data comes from (the Timers page works before the phone is
 * linked too). Shows the last snapshot; asks the phone for news each time it comes up. The watch reaches a PC only through
 * the phone (ADR 45).
 */
class WatchActivity : ComponentActivity() {
    // Opened from the Timers complication or a timer's "Time's up": go to the Timers page once.
    private var openTimers by mutableStateOf(false)

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        // Always 12-hour AM/PM (project convention), whatever the watch's setting.
        use24HourClock = false
        // Bedtime reminders (ADR 38) post notifications: asked once.
        if (checkSelfPermission(android.Manifest.permission.POST_NOTIFICATIONS) != android.content.pm.PackageManager.PERMISSION_GRANTED) {
            requestPermissions(arrayOf(android.Manifest.permission.POST_NOTIFICATIONS), 1)
        }
        openTimers = intent?.getStringExtra(PAGE) == PAGE_TIMERS
        val snapshots = SnapshotStore.snapshot(this)
        setContent {
            MaterialTheme {
                val stored by snapshots.collectAsState()
                val phoneReachable by PhoneLink.phoneReachable.collectAsState()
                WatchApp(
                    stored, phoneReachable, onAskAgain = { askForNews(force = true) }, onForget = { SnapshotStore.clear(this) },
                    openTimers = openTimers, onTimersOpened = { openTimers = false },
                )
            }
        }
        // The timers' alarms and complication follow the stored timers (e.g. after an app update). Off the main thread,
        // so opening the app doesn't wait for them; they only need to be right within a second.
        lifecycleScope.launch(Dispatchers.Default) { TimerStore.refresh(applicationContext) }
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        if (intent.getStringExtra(PAGE) == PAGE_TIMERS) {
            openTimers = true
        }
    }

    override fun onStart() {
        super.onStart()
        askForNews(force = false)
    }

    private fun askForNews(force: Boolean) {
        lifecycleScope.launch {
            val relayAllowed = SnapshotStore.snapshot(this@WatchActivity).value?.relayAllowed ?: true
            // False: asked a moment ago, and what came then is still on screen.
            PhoneLink.askForNews(this@WatchActivity, relayAllowed, force)
        }
    }

    companion object {
        private const val PAGE = "page"
        private const val PAGE_TIMERS = "timers"

        /** Opens the app on the Timers page (the Timers complication, a timer's "Time's up"). */
        fun timersIntent(context: Context): Intent =
            Intent(context, WatchActivity::class.java)
                .putExtra(PAGE, PAGE_TIMERS)
                .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TOP or Intent.FLAG_ACTIVITY_SINGLE_TOP)
    }
}

/**
 * A clock that ticks on the wall-clock second. It is a [State] that only the pages that need a second's resolution read
 * (the session, a running lunch, counting timers), so the rest of the app is not recomposed every second. It waits for a
 * frame first, which doesn't come while the app is stopped (the watch face is showing), so it doesn't wake the app then.
 */
@Composable
private fun rememberSecondClock(): State<Instant> {
    val now = remember { mutableStateOf(Instant.now()) }
    LaunchedEffect(Unit) {
        while (true) {
            withFrameMillis { }
            val current = Instant.now()
            now.value = current
            // Up to the start of the next second, so the countdown doesn't drift or skip.
            delay(1_000 - current.nano / 1_000_000L)
        }
    }
    return now
}

@Composable
private fun WatchApp(
    stored: WatchSnapshot?, phoneReachable: Boolean?, onAskAgain: () -> Unit, onForget: () -> Unit,
    openTimers: Boolean, onTimersOpened: () -> Unit,
) {
    // Not read here: the pieces below read it inside derivedStateOf, so this function recomposes only when what they
    // work out changes (the page order, a timer ending, the time zone), not every second.
    val now = rememberSecondClock()
    var sample by remember { mutableStateOf<WatchSnapshot?>(null) }

    val zone by remember { derivedStateOf { now.value.let { ZoneId.systemDefault() } } }
    val context = LocalContext.current
    // A timer that's just up brings the Timers page to the front (its buzz comes from the alarm).
    val timers by remember { TimerStore.timers(context) }.collectAsState()
    val doneIds by remember { derivedStateOf { timers.filter { it.isDone(now.value) }.map { it.id }.toSet() } }
    var seenDone by remember { mutableStateOf(doneIds) }
    var timersDue by remember { mutableStateOf(false) }
    LaunchedEffect(doneIds) {
        if ((doneIds - seenDone).isNotEmpty()) {
            timersDue = true
        }
        seenDone = doneIds
    }

    AppScaffold {
        val snapshot = stored ?: sample
        val lunch by remember(snapshot, zone) {
            derivedStateOf {
                snapshot?.lunch?.takeIf { snapshot.date == now.value.atZone(zone).toLocalDate() && it.isShown(now.value) }
            }
        }
        // Not linked yet: the "not linked" screen, and the watch's own timers beside it.
        val pages by remember(snapshot) {
            derivedStateOf {
                if (snapshot == null) {
                    listOf(Page.NotLinked, Page.Timers)
                } else {
                    // While a focus or break timer runs, its page comes first; else while lunch runs, the Lunch page (the pager
                    // starts over when the pages change). The Lunch page shows on a workday or while lunch runs, from today's snapshot.
                    val running = snapshot.running?.isLiveAt(now.value) == true
                    val onLunch = lunch?.isRunning(now.value) == true
                    val order = when {
                        running -> listOfNotNull(Page.Session, Page.Today, Page.Lunch.takeIf { lunch != null })
                        onLunch -> listOf(Page.Lunch, Page.Today, Page.Session)
                        else -> listOfNotNull(Page.Today, Page.Session, Page.Lunch.takeIf { lunch != null })
                    }
                    order + listOf(Page.Timers, Page.Noise, Page.Week, Page.Alerts, Page.Link)
                }
            }
        }
        val pager = key(pages) { rememberPagerState(pageCount = { pages.size }) }
        LaunchedEffect(openTimers, timersDue, pager) {
            if (openTimers || timersDue) {
                pager.scrollToPage(pages.indexOf(Page.Timers))
                timersDue = false
                onTimersOpened()
            }
        }
        HorizontalPagerScaffold(pagerState = pager) {
            // The next page on each side is composed ahead, so a swipe doesn't build it mid-gesture.
            HorizontalPager(state = pager, beyondViewportPageCount = 1) { page ->
                AnimatedPage(pageIndex = page, pagerState = pager) {
                    ScreenScaffold {
                        when (pages[page]) {
                            Page.NotLinked -> NotLinkedScreen(phoneReachable, onAskAgain, onSample = { sample = SampleData.snapshot(Instant.now(), zone) })
                            Page.Today -> snapshot?.let { TodayContent(it, now, zone) }
                            Page.Session -> snapshot?.let { SessionContent(it, now, zone) }
                            Page.Lunch -> lunch?.let { com.accountabilibuddy.watch.ui.LunchPage(it, now, zone) }
                            Page.Timers -> TimersPage(now, isCurrent = pager.currentPage == page)
                            Page.Week -> snapshot?.let { WeekContent(it, now, zone) }
                            Page.Noise -> com.accountabilibuddy.watch.ui.NoisePage(isCurrent = pager.currentPage == page)
                            Page.Alerts -> AlertsPage(onChanged = { PhaseAlertScheduler.schedule(context, stored) })
                            Page.Link -> snapshot?.let {
                                LinkContent(it, now, isSample = stored == null, onForget = { if (stored == null) sample = null else onForget() })
                            }
                        }
                    }
                }
            }
        }
    }
}

// The pages below work their view out from the second clock, but only recompose when the view really changes (the views are
// data classes, so an unchanged one counts as the same), e.g. the Today page about once a minute.

@Composable
private fun TodayContent(snapshot: WatchSnapshot, now: State<Instant>, zone: ZoneId) {
    val view by remember(snapshot, zone) { derivedStateOf { TodayView.from(snapshot, now.value, zone, use24HourClock) } }
    TodayPage(view)
}

@Composable
private fun SessionContent(snapshot: WatchSnapshot, now: State<Instant>, zone: ZoneId) {
    val view by remember(snapshot, zone) {
        derivedStateOf {
            SessionView.from(snapshot.running, now.value, zone, use24HourClock, dataAt = snapshot.dataAt, goalFraction = snapshot.goalFraction(now.value, zone))
        }
    }
    SessionPage(view)
}

@Composable
private fun WeekContent(snapshot: WatchSnapshot, now: State<Instant>, zone: ZoneId) {
    val view by remember(snapshot, zone) { derivedStateOf { WeekView.from(snapshot, now.value, zone) } }
    WeekPage(view)
}

/** What the Link page says that depends on the time: how old the data is. */
private data class LinkAges(val updated: String?, val sources: List<String>)

@Composable
private fun LinkContent(snapshot: WatchSnapshot, now: State<Instant>, isSample: Boolean, onForget: () -> Unit) {
    val ages by remember(snapshot) {
        derivedStateOf {
            LinkAges(
                updated = WatchText.updatedAgo(snapshot.dataAt, now.value),
                // Each device whose focus is in the day (ADR 22), with its data's age.
                sources = snapshot.sources.map { source -> "${source.name} · ${WatchText.updatedAgo(source.dataAt, now.value) ?: "up to date"}" },
            )
        }
    }
    LinkPage(pcName = snapshot.pcName, updated = ages.updated, sources = ages.sources, isSample = isSample, onForget = onForget)
}

private enum class Page { NotLinked, Today, Session, Lunch, Timers, Noise, Week, Alerts, Link }
