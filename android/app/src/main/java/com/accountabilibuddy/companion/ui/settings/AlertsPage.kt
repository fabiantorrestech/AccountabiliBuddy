package com.accountabilibuddy.companion.ui.settings

import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.platform.LocalContext
import com.accountabilibuddy.shared.StatusNotification
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
    timelineNotificationPinned: Boolean,
    onTimelineNotification: (Boolean) -> Unit,
    onTimelineNotificationPinned: (Boolean) -> Unit,
    onStatusNotification: (Boolean) -> Unit,
    onAddWidget: () -> Unit,
    noise: NoisePlayer?,
    noiseSettings: NoiseSettings,
) {
    val context = LocalContext.current
    var statusNotification by remember { mutableStateOf(StatusNotification.isEnabled(context)) }
    val timelineSummary = when {
        !timelineNotificationOn -> "Off"
        timelineNotificationPinned -> "Pinned"
        else -> "On"
    }
    val rows = buildList {
        add(PageRow(SettingsAnchors.TIMELINE) {
            Section("Timeline notification", summary = timelineSummary, startOpen = true) {
                ToggleRow("Timeline notification", "Show the day timeline in the notification shade", timelineNotificationOn, onChange = onTimelineNotification)
                ToggleRow(
                    "Pin timeline notification",
                    "Keep the timeline near the top and restore it after a swipe. Background sync and unlock checks continue when unpinned.",
                    timelineNotificationPinned,
                    onChange = onTimelineNotificationPinned,
                )
                AbTonalButton("Add timeline widget to home screen", onClick = onAddWidget)
            }
        })
        add(PageRow(SettingsAnchors.STATUS_NOTIFICATION) {
            ToggleRow("Persistent status notification", "Keep a silent shortcut to the app", statusNotification) {
                onStatusNotification(it)
                statusNotification = it
            }
        })
        if (noise != null) add(PageRow(SettingsAnchors.NOISE) { NoiseSection(noise, noiseSettings) })
        add(PageRow(SettingsAnchors.HEADPHONES) { HeadphonesSection() })
    }
    SettingsPageList(rows, anchor)
}
