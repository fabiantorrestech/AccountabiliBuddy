package com.accountabilibuddy.companion.ui.settings

import com.accountabilibuddy.companion.model.SleepSettings
import com.accountabilibuddy.companion.ui.PauseActions
import java.time.LocalTime

/**
 * What the Settings pages can ask for. The shell fills it in; a page only calls it. [onReenableTime] sets when Workdays' (false)
 * or Off-Days' (true) blocks come back on (ADR 37) and returns null when saved, else why not.
 */
data class SettingsPageActions(
    val onImport: () -> Unit,
    val onClearImport: () -> Unit,
    val onSaveSleep: (SleepSettings) -> Unit,
    val onTimelineNotification: (Boolean) -> Unit,
    val onConnectHealth: () -> Unit,
    val onAddWidget: () -> Unit,
    val onConnectCurbox: () -> Unit,
    val onAllowUsage: () -> Unit,
    val onOpenSetup: () -> Unit,
    val onOpenDevices: () -> Unit,
    val onOpenPlan: () -> Unit,
    val onOpenCurboxSetup: () -> Unit,
    val onOpenWhatsNew: () -> Unit,
    val onReenableTime: (Boolean, LocalTime) -> String?,
    val pause: PauseActions,
)
