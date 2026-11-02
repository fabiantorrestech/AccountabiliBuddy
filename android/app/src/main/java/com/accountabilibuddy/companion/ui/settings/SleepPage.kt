package com.accountabilibuddy.companion.ui.settings

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.engine.ClockFormat
import com.accountabilibuddy.companion.essentials.EssentialsKind
import com.accountabilibuddy.companion.model.SleepSettings
import com.accountabilibuddy.companion.ui.BedtimeRemindersSection
import com.accountabilibuddy.companion.ui.EssentialsSection
import com.accountabilibuddy.companion.ui.HealthConnectSection
import com.accountabilibuddy.companion.ui.HealthStatus
import com.accountabilibuddy.companion.ui.Need
import com.accountabilibuddy.companion.ui.NeedsLine
import com.accountabilibuddy.companion.ui.Section
import com.accountabilibuddy.companion.ui.SmartBedtimeSection
import com.accountabilibuddy.companion.usage.PhoneUsageReader

/**
 * Settings › Sleep: the sleep times, the wake-up alarm, smart bedtime, reminders, bedtime essentials and Health Connect.
 * [wakeAlarm] draws the wake-up alarm rows (they come from the sync settings).
 */
@Composable
fun SleepPage(
    anchor: String?,
    sleep: SleepSettings,
    clock: ClockFormat,
    health: HealthStatus,
    onSaveSleep: (SleepSettings) -> Unit,
    onConnectHealth: () -> Unit,
    onAllowUsage: () -> Unit,
    wakeAlarm: @Composable () -> Unit,
    wakeAlarmSummary: String,
) {
    val context = LocalContext.current
    val usageAccess = remember { PhoneUsageReader(context).hasAccess() }
    val healthOk = health == HealthStatus.Connected
    val healthNeed = Need("Health Connect", healthOk || health == HealthStatus.NotAvailable, onConnectHealth)
    val usageNeed = Need("Usage access", usageAccess, onAllowUsage)
    val rows = listOf(
        PageRow(SettingsAnchors.SLEEP_TIMES) { SleepSection(sleep, clock, onSaveSleep) },
        PageRow(SettingsAnchors.WAKE_ALARM) { Section("Wake-up alarm", summary = wakeAlarmSummary) { wakeAlarm() } },
        PageRow(SettingsAnchors.SMART_BEDTIME) {
            SmartBedtimeSection(clock) { if (sleep.smartWorkdays || sleep.smartOffDays) NeedsLine(listOf(healthNeed, usageNeed), forWhat = "Smart bedtime") }
        },
        PageRow(SettingsAnchors.BEDTIME_REMINDERS) { BedtimeRemindersSection() },
        PageRow(SettingsAnchors.BEDTIME_ESSENTIALS) {
            Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
                NeedsLine(listOf(usageNeed), forWhat = "Bedtime essentials")
                EssentialsSection(EssentialsKind.Bedtime)
            }
        },
        PageRow(SettingsAnchors.HEALTH_CONNECT) { HealthConnectSection(health, onConnectHealth) },
    )
    SettingsPageList(rows, anchor)
}
