package com.accountabilibuddy.companion.ui

/**
 * Whether Today shows the sync light (board 20a): always, only while something is wrong, or never. It only decides what is drawn: the
 * sync itself, the ↻ button and every lock are the same in all three. Per device, kept in the "ui" preferences, never synced.
 */
enum class SyncLightSetting(val label: String) {
    On("On"),
    OnlyWhenWrong("Only when something's wrong"),
    Off("Off");

    /** Whether the light draws for [model] (null: this phone is linked to nothing, so there is no light to draw). */
    fun shows(model: SyncLightModel?): Boolean = when {
        model == null -> false
        this == On -> true
        this == Off -> false
        // Wrong: this phone can't sync, several devices are far behind, or a device's glyph is dimmed (behind or out of reach).
        else -> model.dot == SyncDot.Offline || model.dot == SyncDot.SeveralBehind || model.devices.any { it.dimmed }
    }
}
