package com.accountabilibuddy.companion.ui.settings

import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.padding
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.ui.AboutScreen
import com.accountabilibuddy.companion.ui.AppearanceScreen
import com.accountabilibuddy.companion.ui.DevicesPageInputs
import com.accountabilibuddy.companion.ui.DevicesScreen
import com.accountabilibuddy.companion.ui.SettingsCategory
import com.accountabilibuddy.companion.ui.kit.AbSubpageHeader

/**
 * One Settings page, whichever it is: the "← Settings" crumb and title, then that category's content. Every category is routed
 * here, from one set of inputs and actions, so a wide layout can show this beside the list ([SettingsList]) without knowing any
 * page. [anchor] is a row's key on the page (a search result): the page opens scrolled to it. No Scaffold and no fixed width.
 */
@Composable
fun SettingsDetailHost(
    category: SettingsCategory,
    anchor: String?,
    onBack: () -> Unit,
    inputs: SettingsPageInputs,
    actions: SettingsPageActions,
    devices: DevicesPageInputs,
) {
    // Appearance draws its own header.
    if (category == SettingsCategory.Appearance) {
        AppearanceScreen(onBack = onBack, anchor = anchor)
        return
    }

    SettingsPageFrame(category, onBack) {
        when (category) {
            SettingsCategory.YourDay -> YourDayPage(anchor, actions.onOpenPlan)
            SettingsCategory.Sleep -> SleepPage(
                anchor, inputs.state.sleep, inputs.clock, inputs.health, actions.onSaveSleep, actions.onConnectHealth, actions.onAllowUsage,
                inputs.wakeAlarm, inputs.wakeAlarmSummary,
            )
            SettingsCategory.FocusSessions -> FocusSessionsPage(anchor)
            SettingsCategory.Guards -> GuardsPage(
                anchor, inputs.state, inputs.clock, inputs.pause, actions.pause, inputs.guardsLock, inputs.pcName, inputs.pcState,
                inputs.deviceName, inputs.curbox, actions.onAllowUsage,
            )
            SettingsCategory.Alerts -> AlertsPage(
                anchor, inputs.state.timelineNotificationOn, actions.onTimelineNotification, actions.onAddWidget, inputs.noise, inputs.noiseSettings,
            )
            SettingsCategory.Devices -> DevicesScreen(devices, anchor)
            SettingsCategory.Connections -> ConnectionsPage(anchor, inputs.curbox, actions.onConnectCurbox, actions.onReenableTime)
            SettingsCategory.AppAndData -> AppAndDataPage(anchor, inputs.state, inputs.importing, actions.onImport, actions.onClearImport)
            SettingsCategory.Setup -> SetupPage(
                anchor, inputs.health, actions.onConnectHealth, actions.onAllowUsage, actions.onOpenSetup, actions.onOpenDevices,
            )
            SettingsCategory.About -> AboutScreen(pcName = inputs.pcName, onOpenWhatsNew = actions.onOpenWhatsNew)
            SettingsCategory.Appearance -> Unit
        }
    }
}

/** A Settings page's frame: the "← Settings" crumb and the page's title, then the page. Back goes to the list. */
@Composable
fun SettingsPageFrame(category: SettingsCategory, onBack: () -> Unit, content: @Composable () -> Unit) {
    Column {
        AbSubpageHeader(crumb = "Settings", title = category.title, onBack = onBack, modifier = Modifier.padding(start = 12.dp, end = 12.dp, top = 8.dp))
        content()
    }
}
