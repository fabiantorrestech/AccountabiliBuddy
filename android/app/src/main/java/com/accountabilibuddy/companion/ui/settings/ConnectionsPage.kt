package com.accountabilibuddy.companion.ui.settings

import androidx.compose.runtime.Composable
import androidx.compose.ui.platform.LocalContext
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.curbox.CurboxStatus
import com.accountabilibuddy.companion.ui.BeeminderSection
import com.accountabilibuddy.companion.ui.HealthConnectSection
import com.accountabilibuddy.companion.ui.HealthStatus
import com.accountabilibuddy.companion.ui.OtherBlockersSection

/**
 * Settings › Connections: Beeminder, Curbox's status (its setup is in Guards › Phone guard › Advanced, [onOpenCurboxSetup]), the other
 * blockers and Health Connect (the same card as on Sleep).
 */
@Composable
fun ConnectionsPage(
    anchor: String?,
    curbox: CurboxStatus,
    health: HealthStatus,
    onConnectHealth: () -> Unit,
    onConnectCurbox: () -> Unit,
    onReenableTime: (Boolean, java.time.LocalTime) -> String?,
    onOpenCurboxSetup: () -> Unit,
) {
    val app = LocalContext.current.applicationContext as CompanionApp
    SettingsPageList(
        listOf(
            PageRow(SettingsAnchors.BEEMINDER) { BeeminderSection(connected = app.sync.received.value.any { it.file.beeminder != null }) },
            PageRow(SettingsAnchors.CURBOX) { CurboxSection(curbox, onConnectCurbox, onReenableTime, onOpenCurboxSetup) },
            PageRow(SettingsAnchors.OTHER_BLOCKERS) { OtherBlockersSection() },
            PageRow(SettingsAnchors.HEALTH_CONNECT) { HealthConnectSection(health, onConnectHealth) },
        ),
        anchor,
    )
}
