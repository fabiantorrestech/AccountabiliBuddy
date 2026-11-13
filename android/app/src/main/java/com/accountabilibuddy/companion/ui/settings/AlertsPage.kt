package com.accountabilibuddy.companion.ui.settings

import androidx.compose.runtime.Composable
import com.accountabilibuddy.companion.noise.NoisePlayer
import com.accountabilibuddy.companion.noise.NoiseSettings
import com.accountabilibuddy.companion.ui.HeadphonesSection
import com.accountabilibuddy.companion.ui.Section
import com.accountabilibuddy.companion.ui.ToggleRow
import com.accountabilibuddy.companion.ui.kit.AbTonalButton

/**
 * Settings › Alerts & on-screen: the timeline notification and widget, the noise player and the headphones switches. (The
 * timeline's look, outlined or grayscale, is in Appearance.)
 */
@Composable
fun AlertsPage(
    anchor: String?,
    timelineNotificationOn: Boolean,
    onTimelineNotification: (Boolean) -> Unit,
    onAddWidget: () -> Unit,
    noise: NoisePlayer?,
    noiseSettings: NoiseSettings,
) {
    val rows = buildList {
        add(PageRow(SettingsAnchors.TIMELINE) {
            Section("Timeline notification", summary = if (timelineNotificationOn) "Pinned" else "Off", startOpen = true) {
                ToggleRow("Timeline notification", "Always on, silent, pinned near the top", timelineNotificationOn, onChange = onTimelineNotification)
                AbTonalButton("Add timeline widget to home screen", onClick = onAddWidget)
            }
        })
        add(PageRow(SettingsAnchors.TASK_REMINDERS) { TaskRemindersCard() })
        if (noise != null) add(PageRow(SettingsAnchors.NOISE) { NoiseSection(noise, noiseSettings) })
        add(PageRow(SettingsAnchors.HEADPHONES) { HeadphonesSection() })
    }
    SettingsPageList(rows, anchor)
}
