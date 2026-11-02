package com.accountabilibuddy.companion.ui.settings

import androidx.compose.runtime.Composable
import androidx.compose.ui.platform.LocalContext
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.curbox.CurboxStatus
import com.accountabilibuddy.companion.ui.BeeminderSection
import com.accountabilibuddy.companion.ui.OtherBlockersSection

/** Settings › Connections: Beeminder, Curbox and the other blockers. */
@Composable
fun ConnectionsPage(anchor: String?, curbox: CurboxStatus, onConnectCurbox: () -> Unit, onReenableTime: (Boolean, java.time.LocalTime) -> String?) {
    val app = LocalContext.current.applicationContext as CompanionApp
    SettingsPageList(
        listOf(
            PageRow(SettingsAnchors.BEEMINDER) { BeeminderSection(connected = app.sync.received.value.any { it.file.beeminder != null }) },
            PageRow(SettingsAnchors.CURBOX) { CurboxSection(curbox, onConnectCurbox, onReenableTime) },
            PageRow(SettingsAnchors.OTHER_BLOCKERS) { OtherBlockersSection() },
        ),
        anchor,
    )
}
