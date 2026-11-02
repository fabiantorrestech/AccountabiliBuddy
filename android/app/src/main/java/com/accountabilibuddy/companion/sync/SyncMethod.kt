package com.accountabilibuddy.companion.sync

/**
 * The ways of syncing that bring the PC's file to this phone by themselves: the shared folder, Google Drive and the own relay. QR codes
 * and sync files are not here: they only work when the user scans or opens something by hand, so they never keep this phone seeing a
 * PC session, and turning them off is not turning off the phone's sync.
 */
enum class SyncMethod(val key: String, val label: String) {
    Drive("drive", "Google Drive"),
    Folder("folder", "Shared folder"),
    Relay("relay", "Own relay");

    /** Whether this way is on and has what it needs (a folder chosen, a relay address). */
    fun isLive(settings: PhoneSyncSettings): Boolean = when (this) {
        Drive -> settings.driveOn
        Folder -> settings.folderOn && settings.folderUri != null
        Relay -> settings.relayOn && settings.relayUrl != null
    }

    /** [settings] with this way turned off (what it points at is kept, so turning it on again needs nothing new). */
    fun turnedOff(settings: PhoneSyncSettings): PhoneSyncSettings = when (this) {
        Drive -> settings.copy(driveOn = false)
        Folder -> settings.copy(folderOn = false)
        Relay -> settings.copy(relayOn = false)
    }

    /** [settings] with this way's own fields put back as in [from] and the way on: how a held turn-off stays on. */
    fun keptOn(settings: PhoneSyncSettings, from: PhoneSyncSettings): PhoneSyncSettings = when (this) {
        Drive -> settings.copy(driveOn = true)
        Folder -> settings.copy(folderOn = true, folderUri = from.folderUri)
        Relay -> settings.copy(relayOn = true, relayUrl = from.relayUrl, relayPin = from.relayPin, relayAdmission = from.relayAdmission)
    }

    /** What this way points at, as text: two settings with the same value here are the same way (a new folder or relay is a different one). */
    fun fingerprint(settings: PhoneSyncSettings): String = when (this) {
        Drive -> "drive"
        Folder -> settings.folderUri.orEmpty()
        Relay -> "${settings.relayUrl}|${settings.relayPin}|${settings.relayAdmission}"
    }

    /** Whether this way points at the same place in [a] and [b]. */
    fun sameIn(a: PhoneSyncSettings, b: PhoneSyncSettings): Boolean = fingerprint(a) == fingerprint(b)

    companion object {
        fun live(settings: PhoneSyncSettings): List<SyncMethod> = entries.filter { it.isLive(settings) }

        fun fromKey(key: String?): SyncMethod? = entries.firstOrNull { it.key == key }
    }
}
