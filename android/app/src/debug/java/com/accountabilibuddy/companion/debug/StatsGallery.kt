package com.accountabilibuddy.companion.debug

import android.content.Intent
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.safeDrawingPadding
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import com.accountabilibuddy.companion.engine.SmartBedtime
import com.accountabilibuddy.companion.model.LocalWindow
import com.accountabilibuddy.companion.stats.PhoneDay
import com.accountabilibuddy.companion.stats.StatsData
import com.accountabilibuddy.companion.stats.StatsRange
import com.accountabilibuddy.companion.stats.StatsView
import com.accountabilibuddy.companion.stats.StatsViewsState
import com.accountabilibuddy.companion.ui.HealthStatus
import com.accountabilibuddy.companion.ui.stats.SleepNightModel
import com.accountabilibuddy.companion.ui.stats.StatsPane
import com.accountabilibuddy.companion.ui.stats.StatsPaneActions
import com.accountabilibuddy.companion.ui.stats.StatsPaneModel
import com.accountabilibuddy.companion.ui.theme.Ab
import com.accountabilibuddy.companion.usage.AppUsage
import com.accountabilibuddy.companion.usage.PhoneUsage
import java.time.Duration
import java.time.Instant
import java.time.LocalDateTime
import java.time.ZoneId

/**
 * Debug only: the Stats pane over the demo PC's history with a made-up phone and sleep, for screenshots on the emulator: the real
 * emulator has no phone use or watch sleep to show, and its clock is whatever it is (a Monday, so a week is one day long). Opened through
 * [KitGalleryActivity] with `--es show stats` and: `--es view overview|consistency|...` and `--es range day|week|month|year`, `--ei shift 3`
 * (the clock runs that many days ahead of the real one, with the history moved along), `--ez nopc true` (nothing received: the first
 * empty state), `--ez nodata true` (a PC linked that sent no sessions).
 */
@Composable
fun StatsGallery(intent: Intent) {
    val zone = ZoneId.systemDefault()
    val now = Instant.now().plus(Duration.ofDays(intent.getIntExtra("shift", 0).toLong()))
    val noPc = intent.getBooleanExtra("nopc", false)
    val noData = intent.getBooleanExtra("nodata", false)
    val plan = DemoPcFile.dayPlan("week")
    val files = if (noPc) emptyList() else listOf(DemoPcFile.build(now, withSession = false, methods = listOf("drive")).let { if (noData) it.copy(sessions = emptyList(), drift = emptyList()) else it })
    val data = remember { StatsData.build(files, files, now, zone) { plan.isWorkday(it) } }
    val today = data.today
    val phoneDays = remember {
        listOf(190, 165, 242, 150, 220, 305, 118).mapIndexed { index, minutes ->
            PhoneDay(today.minusDays(6L - index), Duration.ofMinutes(minutes.toLong()), Duration.ofMinutes(minutes / 9L), 38 + index * 4)
        }.takeIf { intent.getBooleanExtra("phone", true) && !noPc } ?: emptyList()
    }
    val nights = remember {
        val windows = (13 downTo 1).filter { it != 6 && it != 7 }.map { back ->
            val evening = today.minusDays(back.toLong())
            val bed = evening.atTime(22, 55).plusMinutes((back * 7 % 40).toLong())
            LocalWindow(bed, bed.plusMinutes(400L + (back * 23 % 80)))
        }
        SmartBedtime.nights(windows, LocalDateTime.of(today, java.time.LocalTime.of(13, 38)), { plan.isWorkdayByWeek(it) }, { _, _ -> null }, { evening -> evening.atTime(23, 0) })
    }
    val usage = PhoneUsage(
        Duration.ofMinutes(118), Duration.ofMinutes(13),
        listOf(AppUsage("a", "Messages", Duration.ofMinutes(41), Duration.ofMinutes(6)), AppUsage("b", "Maps", Duration.ofMinutes(33), Duration.ZERO), AppUsage("c", "Camera", Duration.ofMinutes(12), Duration.ofMinutes(7))),
    ).takeIf { phoneDays.isNotEmpty() }
    val model = remember {
        StatsPaneModel(
            data, !noPc, phoneDays, usage, nights, Duration.ofHours(8),
            SleepNightModel("Planned: 11:00 PM – 7:00 AM (8h 00m)", HealthStatus.Connected, "Last sleep: 7h 12m (11:20 PM – 6:32 AM)"),
        ) { date -> listOf(LocalWindow(date.minusDays(1).atTime(23, 0), date.atTime(7, 0))) }
    }
    var view by remember { mutableStateOf(StatsView.fromKey(intent.getStringExtra("view")) ?: StatsView.Overview) }
    var range by remember { mutableStateOf(StatsRange.entries.firstOrNull { it.name.equals(intent.getStringExtra("range"), ignoreCase = true) }) }
    var views by remember { mutableStateOf(StatsViewsState()) }
    val actions = StatsPaneActions({ view = it }, { range = it }, { views = it }, {}, {}, {})
    Box(Modifier.fillMaxSize().background(Ab.colors.bg).safeDrawingPadding()) { StatsPane(model, view, range, views, actions) }
}
