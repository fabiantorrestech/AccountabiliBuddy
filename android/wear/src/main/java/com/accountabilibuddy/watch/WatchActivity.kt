package com.accountabilibuddy.watch

import android.content.Context
import android.content.Intent
import android.os.Bundle
import android.os.SystemClock
import android.text.format.DateFormat
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.key
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
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
import com.accountabilibuddy.watch.link.PcLink
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
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import java.time.Instant
import java.time.ZoneId

/**
 * The watch app (ADR 21): swipe between today's rings, the running session, lunch (on a workday), the watch's own
 * timers, the noise player, the last 7 days and where the data comes from (the Timers page works before the phone is
 * linked too). Shows the last snapshot; asks the phone for news each time it comes up, and when the phone doesn't answer
 * (and hasn't for 2 minutes), asks the PC itself over Wi-Fi if the watch is paired with it and the phone allows it.
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
        // The timers' alarms and complication follow the stored timers (e.g. after an app update).
        TimerStore.refresh(this)
        val snapshots = SnapshotStore.snapshot(this)
        setContent {
            MaterialTheme {
                val stored by snapshots.collectAsState()
                val phoneReachable by PhoneLink.phoneReachable.collectAsState()
                WatchApp(
                    stored, phoneReachable, PcLink.isPaired(this), onAskAgain = { askForNews(force = true) }, onForget = { SnapshotStore.clear(this) },
                    openTimers = openTimers, onTimersOpened = { openTimers = false },
                )
            }
        }
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

    companion object {
        private const val PAGE = "page"
        private const val PAGE_TIMERS = "timers"

        /** Opens the app on the Timers page (the Timers complication, a timer's "Time's up"). */
        fun timersIntent(context: Context): Intent =
            Intent(context, WatchActivity::class.java)
                .putExtra(PAGE, PAGE_TIMERS)
                .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TOP or Intent.FLAG_ACTIVITY_SINGLE_TOP)

        // The phone answers within seconds (it may first ask the PC); after that the watch tries the PC itself.
        private const val PHONE_ANSWER_WAIT_MS = 12_000L
        private const val PHONE_QUIET_MS = 2 * 60_000L
    }
}

@Composable
private fun WatchApp(
    stored: WatchSnapshot?, phoneReachable: Boolean?, pcPaired: Boolean, onAskAgain: () -> Unit, onForget: () -> Unit,
    openTimers: Boolean, onTimersOpened: () -> Unit,
) {
    var now by remember { mutableStateOf(Instant.now()) }
    var sample by remember { mutableStateOf<WatchSnapshot?>(null) }
    LaunchedEffect(Unit) {
        while (true) {
            delay(1_000)
            now = Instant.now()
        }
    }

    val zone = ZoneId.systemDefault()
    val context = LocalContext.current
    // A timer that's just up brings the Timers page to the front (its buzz comes from the alarm).
    val timers by TimerStore.timers(context).collectAsState()
    val doneIds = timers.filter { it.isDone(now) }.map { it.id }.toSet()
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
        val lunch = snapshot?.lunch?.takeIf { snapshot.date == now.atZone(zone).toLocalDate() && it.isShown(now) }
        // Not linked yet: the "not linked" screen, and the watch's own timers beside it.
        val pages = if (snapshot == null) {
            listOf(Page.NotLinked, Page.Timers)
        } else {
            // While a focus or break timer runs, its page comes first; else while lunch runs, the Lunch page (the pager
            // starts over when the pages change). The Lunch page shows on a workday or while lunch runs, from today's snapshot.
            val running = snapshot.running?.let { it.endsAt?.isAfter(now) ?: true } == true
            val onLunch = lunch?.isRunning(now) == true
            val order = when {
                running -> listOfNotNull(Page.Session, Page.Today, Page.Lunch.takeIf { lunch != null })
                onLunch -> listOf(Page.Lunch, Page.Today, Page.Session)
                else -> listOfNotNull(Page.Today, Page.Session, Page.Lunch.takeIf { lunch != null })
            }
            order + listOf(Page.Timers, Page.Noise, Page.Week, Page.Alerts, Page.Link)
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
            HorizontalPager(state = pager) { page ->
                AnimatedPage(pageIndex = page, pagerState = pager) {
                    ScreenScaffold {
                        when (pages[page]) {
                            Page.NotLinked -> NotLinkedScreen(phoneReachable, onAskAgain, onSample = { sample = SampleData.snapshot(Instant.now(), zone) })
                            Page.Today -> snapshot?.let { TodayPage(TodayView.from(it, now, zone, use24HourClock)) }
                            Page.Session -> snapshot?.let {
                                SessionPage(SessionView.from(it.running, now, zone, use24HourClock, dataAt = it.dataAt, goalFraction = it.goalFraction(now, zone)))
                            }
                            Page.Lunch -> lunch?.let { com.accountabilibuddy.watch.ui.LunchPage(it, now, zone) }
                            Page.Timers -> TimersPage(now, isCurrent = pager.currentPage == page)
                            Page.Week -> snapshot?.let { WeekPage(WeekView.from(it, now, zone)) }
                            Page.Noise -> com.accountabilibuddy.watch.ui.NoisePage(isCurrent = pager.currentPage == page)
                            Page.Alerts -> AlertsPage(onChanged = { PhaseAlertScheduler.schedule(context, stored) })
                            Page.Link -> snapshot?.let {
                                LinkPage(
                                    pcName = it.pcName,
                                    updated = WatchText.updatedAgo(it.dataAt, now),
                                    // Each device whose focus is in the day (ADR 22), with its data's age.
                                    sources = it.sources.map { source -> "${source.name} · ${WatchText.updatedAgo(source.dataAt, now) ?: "up to date"}" },
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
}

private enum class Page { NotLinked, Today, Session, Lunch, Timers, Noise, Week, Alerts, Link }
