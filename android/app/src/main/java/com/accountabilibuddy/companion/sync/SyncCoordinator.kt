package com.accountabilibuddy.companion.sync

import com.accountabilibuddy.shared.sync.SyncCompanion
import com.accountabilibuddy.shared.sync.SyncDeviceCard
import com.accountabilibuddy.shared.sync.SyncFile
import com.accountabilibuddy.shared.sync.SyncPickup
import com.accountabilibuddy.shared.sync.SyncFileCodec
import com.accountabilibuddy.shared.sync.SyncFolder
import com.accountabilibuddy.shared.sync.SyncKey
import com.accountabilibuddy.shared.sync.SyncKeyCode
import com.accountabilibuddy.shared.sync.SyncQr
import kotlinx.coroutines.flow.StateFlow
import java.time.Instant
import java.util.UUID

/**
 * The phone's part in device sync (ADR 22): it joins the group from the PC's link code, takes in files that come by
 * the link, QR codes, Drive or a share, and carries them on ([SyncCarry]): what came by QR code or Drive goes to the
 * PC over the link, and the PC's files go to Drive or out as a share. Its own file holds its card and what it has
 * seen (and its linked watch, Task 18), nothing else.
 */
class SyncCoordinator(
    private val identity: SyncIdentityStore,
    private val store: ReceivedDevicesStore,
    private val deviceName: String,
    private val appVersion: String,
    private val settingsStore: PhoneSyncSettingsStore,
    /** True while a PC is paired (the link is then one of this phone's methods). */
    private val pcPaired: () -> Boolean,
    private val now: () -> Instant = Instant::now,
) {
    private val scan = QrScan()

    /** The other devices' parts on this phone. */
    val received: StateFlow<List<ReceivedDevice>> = store.devices

    /** The group's key; null while the phone isn't in a group. */
    val key: StateFlow<SyncKey?> = identity.key

    /** The phone's method switches (the Devices tab). */
    val settings: StateFlow<PhoneSyncSettings> = settingsStore.settings

    /** True while the phone is in a device group. */
    val isJoined: Boolean
        get() = identity.key.value != null

    fun saveSettings(settings: PhoneSyncSettings) = settingsStore.save(settings)

    /** The group's link code (to show so another device joins); null outside a group. */
    fun linkCode(): String? = identity.code()

    /** This phone's own file as QR code links, for "Show as picture" (a PC without a camera reads a screenshot). */
    fun ownQrLinks(): List<String> {
        val key = identity.key.value ?: return emptyList()
        return SyncQr.links(SyncFileCodec.seal(ownFile(), key), key.groupId)
    }

    // The methods on the phone's card: what's switched on (the link only while a PC is paired).
    private fun methods(): List<String> = settingsStore.settings.value.let { settings ->
        listOfNotNull(
            SyncDeviceCard.METHOD_LINK.takeIf { settings.linkOn && pcPaired() },
            SyncDeviceCard.METHOD_QR.takeIf { settings.qrOn },
            SyncDeviceCard.METHOD_FILE.takeIf { settings.fileOn },
            SyncDeviceCard.METHOD_FOLDER.takeIf { settings.folderOn && settings.folderUri != null },
            SyncDeviceCard.METHOD_DRIVE.takeIf { settings.driveOn },
        )
    }

    /** Called after a file that didn't come over the link was taken in, so it can go on to the PC. */
    var onCarryChanged: (() -> Unit)? = null

    /** The phone's linked watch for its card (Task 18), as the watch bridge last checked it. */
    var companions: () -> List<SyncCompanion> = { emptyList() }

    /** The phone's next alarm for its card (ADR 23); null when there's none. */
    var nextAlarm: () -> Instant? = { null }

    /** Today's flexible lunch (schema v8), when started here or taken from another device. */
    var lunch: () -> com.accountabilibuddy.shared.sync.SyncLunch? = { null }

    /** The phone's unlock times since a moment (ADR 26 pickups); null without Usage access. */
    var unlocksSince: (Instant) -> List<Instant>? = { null }

    /** The pickups during the other devices' counted sessions of the last 14 days; null without Usage access. */
    fun pickups(): List<SyncPickup>? {
        val now = now()
        val sessions = store.devices.value
            .filter { it.file.device.kind != SyncDeviceCard.KIND_PHONE }
            .flatMap { device -> device.file.sessions.map { device.file.device.deviceId to it } }
        val from = sessions.minOfOrNull { it.second.start } ?: return emptyList()
        val unlocks = unlocksSince(from) ?: return null
        return SyncPickup.count(sessions, unlocks, now)
    }

    /** Joins the group of [code] (from the PC's link code); a new group drops the old one's copies. False when invalid. */
    fun join(code: String): Boolean {
        val current = identity.code()
        if (current != null && SyncKeyCode.parse(current)?.groupId == SyncKeyCode.parse(code)?.groupId) {
            return SyncKeyCode.parse(code) != null
        }

        if (!identity.join(code)) {
            return false
        }

        store.clear()
        scan.reset()
        return true
    }

    /** Leaves the group and forgets every device's part on this phone (ADR 15). */
    fun leave() {
        identity.leave()
        store.clear()
        scan.reset()
    }

    /**
     * Takes in a file another device sealed, carried by [method] ([SyncDeviceCard.METHOD_LINK], QR, folder or file).
     * Null when taken in (or already held); otherwise why not.
     */
    @Synchronized
    fun acceptSealed(bytes: ByteArray, method: String): String? {
        val settings = settingsStore.settings.value
        when {
            method == SyncDeviceCard.METHOD_LINK && !settings.linkOn -> return "Sync files over the link are off on this phone."
            method == SyncDeviceCard.METHOD_FILE && !settings.fileOn -> return "Turn on \"Sync files\" in Devices to open sync files."
            method == SyncDeviceCard.METHOD_FOLDER && !settings.folderOn -> return "The sync folder is off on this phone."
        }

        val key = identity.key.value ?: return NOT_JOINED
        val file = SyncFileCodec.open(bytes, key) ?: return CANNOT_READ
        val outcome = SyncCarry.accept(store.devices.value, file, bytes, method, identity.deviceId, now())
        if (outcome.error != null) {
            return outcome.error
        }

        store.save(outcome.devices)
        if (method != SyncDeviceCard.METHOD_LINK) {
            onCarryChanged?.invoke()
        }

        return null
    }

    /** One scanned QR link from the PC: e.g. "2 of 3 scanned", "Synced Work PC", or why not. */
    fun onQrLink(link: String): String {
        if (!settingsStore.settings.value.qrOn) {
            return "Turn on \"QR codes\" in Devices to scan the PC's codes."
        }

        val key = identity.key.value ?: return "Join your devices first: scan the PC's link code (Devices on the PC)."
        val step = synchronized(scan) { scan.add(link, key.groupId) }
        if (step !is QrScanStep.Complete) {
            return step.text ?: ""
        }

        val error = acceptSealed(step.sealed, SyncDeviceCard.METHOD_QR)
        val name = store.devices.value.firstOrNull { it.sealed === step.sealed }?.file?.device?.name
        return error ?: "Synced ${name ?: "your other device"}"
    }

    /** This phone's own file: its card and, for each device, the newest file it holds (nothing else). */
    fun ownFile(): SyncFile = SyncFile(
        SyncFile.CURRENT_FORMAT_VERSION,
        SyncDeviceCard(
            identity.deviceId, deviceName, SyncDeviceCard.KIND_PHONE, appVersion, now(), methods(), false, SyncCarry.seen(store.devices.value), companions(),
            nextAlarm().takeIf { settingsStore.settings.value.alarmOn },
        ),
        emptyList(), emptyList(), null, emptyList(),
        pickups = runCatching { pickups() }.getOrNull(),
        lunch = lunch(),
    )

    /** Everything to hand over by [method] (Drive or a share): this phone's own file first, then what it carries. */
    fun filesToCarry(method: String): List<ByteArray> =
        listOfNotNull(ownSealed()) + SyncCarry.filesFor(store.devices.value, method, emptyMap())

    /**
     * For the linked PC: this phone's own file and what didn't come from the link, unless the PC's own file (by its
     * name) says it has it already.
     */
    fun filesForPc(pcName: String): List<ByteArray> {
        if (!settingsStore.settings.value.linkOn) {
            return emptyList()
        }

        val pcSeen = store.devices.value
            .firstOrNull { it.method == SyncDeviceCard.METHOD_LINK && it.file.device.kind == SyncDeviceCard.KIND_PC && it.file.device.name == pcName }
            ?.file?.device?.seen.orEmpty()
        return listOfNotNull(ownSealed()) + SyncCarry.filesFor(store.devices.value, SyncDeviceCard.METHOD_LINK, pcSeen)
    }

    /** This phone's file name in the sync folder (the PCs' naming); null outside a group. */
    fun ownFolderFileName(): String? = identity.key.value?.let { SyncFolder.fileNameFor(it.groupId, identity.deviceId) }

    /** The card of a sealed file from this group, without taking it in; null when it can't be read. */
    fun cardOf(bytes: ByteArray): SyncDeviceCard? = identity.key.value?.let { SyncFileCodec.open(bytes, it)?.device }

    /**
     * For the sync folder (or Drive, [cameBy]): this phone's own file, and what it carries that didn't come that way and
     * is newer than that device's file there ([inFolder]), each under its device's folder name.
     */
    fun filesForFolder(inFolder: Map<UUID, Instant>, cameBy: String = SyncDeviceCard.METHOD_FOLDER): List<FolderFile> {
        val key = identity.key.value ?: return emptyList()
        val own = ownFile()
        val carried = store.devices.value
            .filter { it.method != cameBy }
            .filter { device -> inFolder[device.file.device.deviceId]?.let { device.file.device.writtenAt > it } ?: true }
            .map { FolderFile(SyncFolder.fileNameFor(key.groupId, it.file.device.deviceId), it.file.device.deviceId, it.file.device.writtenAt, it.sealed) }
        return listOf(FolderFile(SyncFolder.fileNameFor(key.groupId, identity.deviceId), identity.deviceId, own.device.writtenAt, SyncFileCodec.seal(own, key))) + carried
    }

    private fun ownSealed(): ByteArray? = identity.key.value?.let { SyncFileCodec.seal(ownFile(), it) }

    companion object {
        const val NOT_JOINED = "This phone isn't linked to your other devices yet."
        const val CANNOT_READ = "This file can't be read: it's from another device group, or it's damaged."
    }
}
