package com.accountabilibuddy.companion.ui.theme

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.platform.LocalContext
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.compose.LifecycleEventEffect
import kotlinx.coroutines.delay
import java.time.Duration
import java.time.LocalTime
import java.time.ZonedDateTime

// A backstop: even when no switch is near, the plan is looked at again this often (a time zone or daylight-saving change
// raises no event while the app is asleep).
private val LongestWait = Duration.ofHours(3)

// The wait runs a second past the switch, so the plan then sees it as passed.
private val Margin = Duration.ofSeconds(1)

/**
 * Whether the app should look dark right now, for [mode]. Light, Dark and System need no timer; the schedule modes follow
 * [rememberDarkPlan].
 */
@Composable
fun rememberDarkNow(mode: LightDarkMode, darkAt: LocalTime, lightAt: LocalTime, place: AppearancePlace?): Boolean {
    val systemDark = isSystemInDarkTheme()
    val plan = rememberDarkPlan(mode, darkAt, lightAt, place)
    return when (mode) {
        LightDarkMode.Light -> false
        LightDarkMode.Dark -> true
        LightDarkMode.System -> systemDark
        LightDarkMode.SetTime, LightDarkMode.Sunset -> plan?.isDark ?: systemDark
    }
}

/**
 * The plan for At a set time and After sunset (null for the other modes): whether it is dark now and when it next flips. It
 * uses one timer, set for the next switch (never polling), and is worked out again when the screen comes back, after the
 * clock, date or time zone changes, and when a setting changes.
 */
@Composable
fun rememberDarkPlan(mode: LightDarkMode, darkAt: LocalTime, lightAt: LocalTime, place: AppearancePlace?): DarkPlan? {
    val scheduled = mode == LightDarkMode.SetTime || mode == LightDarkMode.Sunset
    val context = LocalContext.current
    var tick by remember { mutableIntStateOf(0) }
    LifecycleEventEffect(Lifecycle.Event.ON_RESUME) { tick++ }
    DisposableEffect(context, scheduled) {
        if (!scheduled) return@DisposableEffect onDispose { }
        val receiver = object : BroadcastReceiver() {
            override fun onReceive(context: Context?, intent: Intent?) {
                tick++
            }
        }
        val filter = IntentFilter().apply {
            addAction(Intent.ACTION_TIME_CHANGED)
            addAction(Intent.ACTION_TIMEZONE_CHANGED)
            addAction(Intent.ACTION_DATE_CHANGED)
        }
        context.registerReceiver(receiver, filter)
        onDispose { context.unregisterReceiver(receiver) }
    }

    var plan by remember(mode, darkAt, lightAt, place, tick) { mutableStateOf(AppearancePlanner.plan(mode, darkAt, lightAt, place)) }
    LaunchedEffect(mode, darkAt, lightAt, place, tick) {
        while (scheduled) {
            val now = ZonedDateTime.now()
            val wait = Duration.between(now, plan?.nextSwitch ?: now.plus(LongestWait)).plus(Margin)
            delay(wait.coerceAtLeast(Margin).coerceAtMost(LongestWait).toMillis())
            plan = AppearancePlanner.plan(mode, darkAt, lightAt, place)
        }
    }
    return if (scheduled) plan else null
}

private fun Duration.coerceAtLeast(min: Duration): Duration = if (this < min) min else this

private fun Duration.coerceAtMost(max: Duration): Duration = if (this > max) max else this
