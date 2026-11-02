package com.accountabilibuddy.companion.ui.settings

import com.accountabilibuddy.companion.curbox.CurboxStatus
import com.accountabilibuddy.companion.data.CompanionState
import com.accountabilibuddy.companion.engine.ClockFormat
import com.accountabilibuddy.companion.noise.NoisePlayer
import com.accountabilibuddy.companion.noise.NoiseSettings
import com.accountabilibuddy.companion.pause.PauseState
import com.accountabilibuddy.companion.placement.GuardsLock
import com.accountabilibuddy.companion.sync.PcState
import com.accountabilibuddy.companion.ui.HealthStatus

/**
 * What the Settings pages read, as the shell has it. One holder so [SettingsDetailHost] routes every page from the same two
 * objects (this and [SettingsPageActions]) whatever the layout around it is.
 *
 * @property pcName The PC this phone controls, for the Guards page.
 * @property deviceName This phone's name, for the Guards page.
 * @property wakeAlarm The wake-up alarm rows (they come from the sync settings), drawn on the Sleep page.
 * @property wakeAlarmSummary "On: …" or "Off: …", the wake-up alarm card's summary.
 */
data class SettingsPageInputs(
    val state: CompanionState,
    val clock: ClockFormat,
    val health: HealthStatus,
    val importing: Boolean,
    val curbox: CurboxStatus,
    val noise: NoisePlayer?,
    val noiseSettings: NoiseSettings,
    val pause: PauseState?,
    val guardsLock: GuardsLock?,
    val pcName: String?,
    val pcState: PcState?,
    val deviceName: String,
    val wakeAlarm: @androidx.compose.runtime.Composable () -> Unit,
    val wakeAlarmSummary: String,
)
