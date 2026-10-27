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
 * QR codes, a sync folder, Drive or a share, and carries them on ([SyncCarry]): what came by QR code or a share goes to
 * Drive, and the PC's files go to Drive or out as a share. Its own file holds its card and what it has seen (and its
 * linked watch, Task 18), nothing else.
 */
class SyncCoordinator(
    private val identity: SyncIdentityStore,
    private val store: ReceivedDevicesStore,
    private val deviceName: String,
    private val appVersion: String,
    private val settingsStore: PhoneSyncSettingsStore,
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
        // The shared lists with their tombstones (ADR 48) stay out of QR codes, as on the PC: a file without them leaves them alone.
        return SyncQr.links(SyncFileCodec.seal(ownFile().copy(nextTasks = null, parked = null), key), key.groupId)
    }

    // The methods on the phone's card: what's switched on.
    private fun methods(): List<String> = settingsStore.settings.value.let { settings ->
        listOfNotNull(
            SyncDeviceCard.METHOD_QR.takeIf { settings.qrOn },
            SyncDeviceCard.METHOD_FILE.takeIf { settings.fileOn },
            SyncDeviceCard.METHOD_FOLDER.takeIf { settings.folderOn && settings.folderUri != null },
            SyncDeviceCard.METHOD_DRIVE.takeIf { settings.driveOn },
            SyncDeviceCard.METHOD_RELAY.takeIf { settings.relayOn && settings.relayUrl != null },
        )
    }

    /**
     * Called with the method a file came by after it was taken in and only when it brought something new (a device not
     * held before, or a file newer than the copy held), so it can go on through the other network methods (a file
     * that didn't come by Drive goes on through Drive, one that didn't come by the relay through the relay).
     */
    var onCarryChanged: ((String) -> Unit)? = null

    /** The phone's linked watch for its card (Task 18), as the watch bridge last checked it. */
    var companions: () -> List<SyncCompanion> = { emptyList() }

    /** The phone's next alarm for its card (ADR 23); null when there's none. */
    var nextAlarm: () -> Instant? = { null }

    /** This phone's sleep settings as a document, once changed here (ADR 35); null before. */
    var sleepDocument: () -> com.accountabilibuddy.shared.sync.SyncDocument? = { null }

    /** Curbox's blocks as last read (ADR 37); null when never read. */
    var blockers: () -> List<com.accountabilibuddy.shared.sync.SyncBlocker>? = { null }

    /** Bedtime reminders dismissed or snoozed here in the last day (ADR 38). */
    var reminderActions: () -> List<com.accountabilibuddy.shared.ReminderMessages.Action> = { emptyList() }

    /** The smart bedtime's share for the PCs (ADR 34); null when it's off. */
    var smartBedtime: () -> com.accountabilibuddy.shared.sync.SyncSmartBedtime? = { null }

    /** The estimated wake times for the PCs (ADR 40); null when there's none. */
    var wakeEstimate: () -> com.accountabilibuddy.shared.sync.SyncWakeEstimate? = { null }

    /** Today's flexible lunch (schema v8), when started here or taken from another device. */
    var lunch: () -> com.accountabilibuddy.shared.sync.SyncLunch? = { null }

    /** The next session's task list with its tombstones (ADR 48); null before the app sets it. */
    var nextTasks: () -> List<com.accountabilibuddy.shared.sync.SyncNextTask>? = { null }

    /** The come-backs with their tombstones (ADR 48); null before the app sets it. */
    var parked: () -> List<com.accountabilibuddy.shared.sync.SyncParked>? = { null }

    /**
     * Another device's shared lists (ADR 48), from a file just taken in; a list the file doesn't carry is null. The app
     * merges them into its own. Called with the lists only, so the copy kept of the file doesn't hold their text.
     */
    var onSharedLists: (List<com.accountabilibuddy.shared.sync.SyncNextTask>?, List<com.accountabilibuddy.shared.sync.SyncParked>?) -> Unit = { _, _ -> }

    /** What the user did here for a PC, waiting for its ack (ADR 26); set by the app. */
    var actions: () -> List<com.accountabilibuddy.shared.sync.SyncAction> = { emptyList() }

    /** The phone's unlock times since a moment (ADR 26 pickups); null without Usage access. */
    var unlocksSince: (Instant) -> List<Instant>? = { null }

    /** Emergency phone time (start, end), counted apart from pickups; set by the app. */
    var emergencyWindows: () -> List<Pair<Instant, Instant>> = { emptyList() }

    /** Where the phone is during a PC session (phase 11b): label, times and counts; null when there's none. */
    var placement: () -> com.accountabilibuddy.shared.sync.SyncPlacement? = { null }

    /** True while a PC focus session runs (phase 11b), so the phone's file is rewritten every 2 minutes for the placement; set by the app. */
    var inFocusSession: () -> Boolean = { false }

    /** The pickups during the other devices' counted sessions of the last 14 days; null without Usage access. */
    fun pickups(): List<SyncPickup>? {
        val now = now()
        val sessions = store.devices.value
            .filter { it.file.device.kind != SyncDeviceCard.KIND_PHONE }
            .flatMap { device -> device.file.sessions.map { device.file.device.deviceId to it } }
        val from = sessions.minOfOrNull { it.second.start } ?: return emptyList()
        val unlocks = unlocksSince(from) ?: return null
        return SyncPickup.count(sessions, unlocks, now, emergencyWindows())
    }

    /** Joins the group of [code] (from the PC's link code); a new group drops the old one's copies. False when invalid. */
    fun join(code: String): Boolean {
        val current = identity.code()
        if (current != null && SyncKeyCode.parse(current)?.sameGroupAs(SyncKeyCode.parse(code)) == true) {
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
     * Takes in a file another device sealed, carried by [method] (QR, folder, Drive, relay or file).
     * Null when taken in (or already held); otherwise why not.
     */
    @Synchronized
    fun acceptSealed(bytes: ByteArray, method: String): String? {
        val settings = settingsStore.settings.value
        when {
            method == SyncDeviceCard.METHOD_FILE && !settings.fileOn -> return "Turn on \"Sync files\" in Devices to open sync files."
            method == SyncDeviceCard.METHOD_FOLDER && !settings.folderOn -> return "The sync folder is off on this phone."
            method == SyncDeviceCard.METHOD_RELAY && !settings.relayOn -> return "The relay is off on this phone."
        }

        val key = identity.key.value ?: return NOT_JOINED
        val file = SyncFileCodec.open(bytes, key) ?: return CANNOT_READ
        val held = store.devices.value.firstOrNull { it.file.device.deviceId == file.device.deviceId }
        // The copy kept of the sender's file doesn't hold the shared lists (ADR 48): they merge into this phone's own sealed
        // store below, so their text isn't kept a second time in the clear. The sealed bytes it carries on are sealed.
        // An empty "parked" stays as the mark that the device keeps the shared lists (a QR file carries none: the mark is kept).
        val mark = if (file.parked != null || held?.file?.parked != null) emptyList<com.accountabilibuddy.shared.sync.SyncParked>() else null
        val outcome = SyncCarry.accept(store.devices.value, file.copy(nextTasks = null, parked = mark), bytes, method, identity.deviceId, now())
        if (outcome.error != null) {
            return outcome.error
        }

        store.save(outcome.devices)
        // Not from a device whose clock runs more than a minute ahead; a later file from it, once the clocks agree, is taken.
        if ((file.nextTasks != null || file.parked != null) && !file.device.writtenAt.isAfter(now().plusSeconds(60))) {
            onSharedLists(file.nextTasks, file.parked)
        }

        if (held == null || file.device.writtenAt > held.file.device.writtenAt) {
            onCarryChanged?.invoke(method)
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
        emptyList(), emptyList(), null, listOfNotNull(runCatching { sleepDocument() }.getOrNull()),
        pickups = runCatching { pickups() }.getOrNull(),
        lunch = lunch(),
        actions = actions().takeIf { it.isNotEmpty() },
        smartBedtime = runCatching { smartBedtime() }.getOrNull(),
        reminderActions = runCatching { reminderActions() }.getOrNull()?.takeIf { it.isNotEmpty() },
        blockers = runCatching { blockers() }.getOrNull(),
        wakeEstimate = runCatching { wakeEstimate() }.getOrNull(),
        placement = runCatching { placement() }.getOrNull(),
        nextTasks = runCatching { nextTasks() }.getOrNull(),
        parked = runCatching { parked() }.getOrNull(),
    )

    /** Everything to hand over by [method] (Drive or a share): this phone's own file first, then what it carries. */
    fun filesToCarry(method: String): List<ByteArray> =
        listOfNotNull(ownSealed()) + SyncCarry.filesFor(store.devices.value, method, emptyMap())

    /** This phone's file name in the sync folder (the PCs' naming); null outside a group. */
    fun ownFolderFileName(): String? = identity.key.value?.let { SyncFolder.fileNameFor(it.groupId, identity.deviceId) }

    /** The card of a sealed file from this group, without taking it in; null when it can't be read. */
    fun cardOf(bytes: ByteArray): SyncDeviceCard? = identity.key.value?.let { SyncFileCodec.open(bytes, it)?.device }

    /**
     * For the sync folder (or Drive or the relay, [cameBy]): this phone's own file, and what it carries that didn't come that way and
     * is newer than that device's file there ([inFolder]), each under its device's folder name. The own file is left out
     * while [gate] says nothing changed (see [OwnFileWriteGate]); call [OwnFileWriteGate.written] after writing it.
     */
    fun filesForFolder(inFolder: Map<UUID, Instant>, gate: OwnFileWriteGate, cameBy: String = SyncDeviceCard.METHOD_FOLDER): List<FolderFile> {
        val key = identity.key.value ?: return emptyList()
        val own = ownFile()
        val carried = store.devices.value
            .filter { it.method != cameBy }
            .filter { device -> inFolder[device.file.device.deviceId]?.let { device.file.device.writtenAt > it } ?: true }
            .map { FolderFile(SyncFolder.fileNameFor(key.groupId, it.file.device.deviceId), it.file.device.deviceId, it.file.device.writtenAt, it.sealed) }
        val ownFile = if (gate.due(own, now(), inFocusSession())) {
            listOf(FolderFile(SyncFolder.fileNameFor(key.groupId, identity.deviceId), identity.deviceId, own.device.writtenAt, SyncFileCodec.seal(own, key), own = true))
        } else {
            emptyList()
        }
        return ownFile + carried
    }

    private fun ownSealed(): ByteArray? = identity.key.value?.let { SyncFileCodec.seal(ownFile(), it) }

    companion object {
        const val NOT_JOINED = "This phone isn't linked to your other devices yet."
        const val CANNOT_READ = "This file can't be read: it's from another device group, or it's damaged."
    }
}
