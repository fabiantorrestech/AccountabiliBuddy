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
        // Wrong: this phone can't sync, several devices are far behind, one way of syncing has a problem (the popover names it), or a device
        // is behind. A watch out of Bluetooth range dims its glyph but is not "wrong" (it is a distance, not a fault).
        else -> model.dot == SyncDot.Offline || model.dot == SyncDot.SeveralBehind || model.needsAttention ||
            model.devices.any { it.dimmed && it.kind != DeviceKind.WATCH }
    }
}
