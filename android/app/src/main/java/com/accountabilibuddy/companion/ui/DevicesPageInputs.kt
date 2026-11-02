package com.accountabilibuddy.companion.ui

/**
 * What Settings › Devices & sync is drawn from and can ask for: the devices and their state, the actions on them, the linked
 * watch, and the PC this phone controls.
 *
 * @property pcChoices The PCs whose files are here (sync device id to name, newest first), for picking the one to control (ADR 42).
 * @property chosenPcId The PC picked; null: the newest PC file's.
 * @property syncNow The "Sync now" holder, shared with the sync light's ↻.
 */
data class DevicesPageInputs(
    val devices: DevicesState,
    val actions: DevicesActions,
    val watch: WatchSectionState,
    val watchActions: WatchSectionActions,
    val pcChoices: List<Pair<String, String>>,
    val chosenPcId: String?,
    val onChoosePc: (String?) -> Unit,
    val syncNow: SyncNow,
)
