package com.accountabilibuddy.companion.sync

import com.accountabilibuddy.shared.sync.RekeyBuilder
import com.accountabilibuddy.shared.sync.RekeyCodec
import com.accountabilibuddy.shared.sync.RekeyReader
import com.accountabilibuddy.shared.sync.RekeyReadingKind
import com.accountabilibuddy.shared.sync.RekeyRecipient
import com.accountabilibuddy.shared.sync.RekeyRemovedDevice
import com.accountabilibuddy.shared.sync.SyncCompanion
import com.accountabilibuddy.shared.sync.SyncDeviceCard
import com.accountabilibuddy.shared.sync.SyncFile
import com.accountabilibuddy.shared.sync.SyncPickup
import com.accountabilibuddy.shared.sync.SyncFileCodec
import com.accountabilibuddy.shared.sync.SyncFolder
import com.accountabilibuddy.shared.sync.SyncKey
import com.accountabilibuddy.shared.sync.SyncKeyCode
import com.accountabilibuddy.shared.sync.SyncQr
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.launch
import java.time.Instant
import java.util.UUID

/**
 * The phone's part in device sync (ADR 22): it joins the group from the PC's link code, takes in files that come by
 * QR codes, a sync folder, Drive or a share, and carries them on ([SyncCarry]): what came by QR code or a share goes to
 * Drive, and the PC's files go to Drive or out as a share. Its own file holds its card and what it has seen (and its
 * linked watch, Task 18), nothing else.
 *
 * It also removes devices from the group and learns of a removal (ADR 54): [removeDevices] writes the rekey file and switches
 * to a new key; a rekey file that comes in switches this phone the same way, keeping what it received, or, when it names
 * this phone, leaves it [unlinked] with nothing syncing until the user chooses.
 */
class SyncCoordinator(
    private val identity: SyncIdentityStore,
    private val store: ReceivedDevicesStore,
    private val removedStore: RemovedDevicesStore,
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

    /** The devices removed from the group (ADR 54); their files are refused and never carried. */
    val removedDevices: StateFlow<List<RemovedDevice>> = removedStore.devices

    /** Set while this phone is unlinked (removed from its group, or cut off by a removal); null otherwise. The UI asks the user to choose. */
    val unlinked: StateFlow<UnlinkedState?> = removedStore.unlinked

    /**
     * Counts every change of the group's key (joining, leaving, a new key after a removal, being unlinked), so a sync source
     * can drop what it remembered about the old group's files and read the new group's from scratch.
     */
    @Volatile
    var keyGeneration: Int = 0
        private set

    /** The folder, Drive and the relay, which carry the rekey file and clear old files (ADR 54); set by the app. */
    var rekeyMethods: List<RekeySyncMethod> = emptyList()

    /** Where the rekey is forwarded and the old group cleared in the background; set by the app. */
    var backgroundScope: CoroutineScope? = null

    /** Why removing devices or leaving is refused right now (e.g. a focus session the user started); null when it's allowed. Set by the app. */
    var removalLockedReason: () -> String? = { null }

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
        return SyncQr.links(SyncFileCodec.seal(ownFile().copy(nextTasks = null, parked = null, later = null), key), key.groupId)
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

    /** The Later list with its tombstones (ADR 50); null before the app sets it. */
    var later: () -> List<com.accountabilibuddy.shared.sync.SyncLaterItem>? = { null }

    /**
     * Another device's shared lists (ADR 48), from a file just taken in; a list the file doesn't carry is null. The app
     * merges them into its own. Called with the lists only, so the copy kept of the file doesn't hold their text.
     */
    var onSharedLists: (
        List<com.accountabilibuddy.shared.sync.SyncNextTask>?,
        List<com.accountabilibuddy.shared.sync.SyncParked>?,
        List<com.accountabilibuddy.shared.sync.SyncLaterItem>?,
        Boolean,
    ) -> Unit = { _, _, _, _ -> }

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

    /**
     * Joins the group of [code] (from the PC's link code); a new group drops the old one's copies. False when invalid.
     * It is also "Add this phone again" after a removal: the device id stays, and being [unlinked] ends.
     */
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
        removedStore.setUnlinked(null)
        keyGeneration++
        return true
    }

    /** Leaves the group and forgets every device's part on this phone (ADR 15). */
    fun leave() {
        identity.leave()
        store.clear()
        scan.reset()
        keyGeneration++
    }

    /**
     * "Continue unsynced here" (ADR 54): this phone stays out of the group and keeps its own data. Like [leave] it forgets what
     * was received from the group's devices; it also forgets the removed list, which belonged to the group.
     *
     * @return Null when done; otherwise why not (a focus session the user started).
     */
    fun continueUnsynced(): String? {
        removalLockedReason()?.let { return it }
        removedStore.clearAll()
        leave()
        return null
    }

    /**
     * Applies the user's choice for a removed device's received data (ADR 55): [RemovedDevice.ERASE_DATA] forgets what this
     * phone received from it; [RemovedDevice.KEEP_DATA] keeps it, shown as removed. The choice is recorded either way.
     */
    @Synchronized
    fun applyDataChoice(deviceId: UUID, choice: String) {
        if (choice == RemovedDevice.ERASE_DATA) {
            store.save(store.devices.value.filter { it.file.device.deviceId != deviceId })
        }

        removedStore.setDataChoice(deviceId, choice)
    }

    /**
     * Removes [deviceIds] from the group (ADR 54): makes a new link code, writes the rekey file to every sync method in use,
     * then switches this phone to the new key, keeping everything it received. Their received data is left as it is;
     * [applyDataChoice] is the caller's next step. Nothing changes when it can't be done, or when no method took the file
     * (the other devices would never hear of it).
     */
    suspend fun removeDevices(deviceIds: List<UUID>): RemovalResult {
        removalLockedReason()?.let { return RemovalResult.failed(it) }
        val oldKey = identity.key.value ?: return RemovalResult.failed(NOT_JOINED)
        val ids = deviceIds.distinct()
        if (ids.isEmpty() || identity.deviceId in ids) {
            return RemovalResult.failed("Pick other devices to remove; this phone leaves the group with Leave group.")
        }

        val cards = store.devices.value.map { it.file.device }
        val removing = ids.map { id -> RekeyRemovedDevice(id, cards.firstOrNull { it.deviceId == id }?.name ?: "Unknown device") }
        val staying = cards.filter { it.deviceId !in ids && removedStore.devices.value.none { removed -> removed.deviceId == it.deviceId } }
        val recipients = staying.mapNotNull { card -> card.publicKey?.takeIf { it.isNotEmpty() }?.let { RekeyRecipient(card.deviceId, it) } }
        val build = RekeyBuilder.build(oldKey, RekeyRemovedDevice(identity.deviceId, deviceName), removing, recipients, now())
        // The devices that stay but can't get the new key: an app from before ADR 54 (no public key in its card), or a key
        // that isn't a P-256 point. They are cut off like the removed ones; the remove dialog names them.
        val notWrapped = staying.filter { it.publicKey.isNullOrEmpty() }.map { it.deviceId } + build.notWrapped

        val fileName = RekeyCodec.fileNameFor(oldKey.groupId)
        var written = 0
        var failed = 0
        for (method in rekeyMethods) {
            when (method.publishRekey(build.sealedFile, fileName, oldKey)) {
                true -> written++
                false -> failed++
                null -> Unit
            }
        }

        if (written == 0) {
            return RemovalResult.failed(
                if (failed == 0) "Turn on a sync method (a sync folder, Google Drive or the relay) first: the other devices learn of the change through it."
                else "The change couldn't be written to any sync method. Check the sync status and try again.",
            )
        }

        synchronized(this) {
            removedStore.record(removing.map { it.deviceId to it.name }, now())
            switchTo(build.newCode, oldKey, removing.map { it.deviceId })
        }

        return RemovalResult(true, null, notWrapped, failed)
    }

    /**
     * Takes in a rekey file (ADR 54) another device wrote, from [method]. A rekey for a group this phone has already left or
     * switched away from is ignored, which also makes a repeated file harmless. When it names this phone (or leaves it out) this
     * phone becomes [unlinked]: the key is dropped, so nothing syncs by any method, and what was received stays.
     * Null when taken in or ignored; otherwise why not.
     */
    private fun acceptRekey(bytes: ByteArray, method: String): String? {
        val current = identity.key.value ?: return NOT_JOINED
        if (RekeyCodec.groupIdOf(bytes) != current.groupId) {
            return null
        }

        val payload = RekeyCodec.open(bytes, current) ?: return CANNOT_READ
        val reading = RekeyReader.read(payload, identity.deviceId, identity.deviceKey, current.groupId)
        removedStore.record(payload.removed.filter { it.deviceId != identity.deviceId }.map { it.deviceId to it.name }, now())
        val newCode = reading.newCode
        if (reading.kind == RekeyReadingKind.NewKey && newCode != null) {
            // Hand the file on to the methods it didn't come by, in the old group where the devices that haven't switched yet
            // still listen (the PCs that reach only one of them).
            forwardRekey(bytes, method, current)
            switchTo(newCode, current, payload.removed.map { it.deviceId })
        } else {
            // Removed, or left out of the rekey: there is no new key to switch to.
            removedStore.setUnlinked(UnlinkedState(now(), reading.kind == RekeyReadingKind.Removed, payload.removedBy.name))
            identity.leave()
            scan.reset()
            keyGeneration++
        }

        return null
    }

    // Switches to the new link code in place: what was received stays, but its sealed bytes can't be opened by the others any
    // more, so those copies aren't carried until the device's next file replaces them. The old group's files for this phone and
    // the removed devices are deleted in the background.
    private fun switchTo(newCode: String, oldKey: com.accountabilibuddy.shared.sync.SyncKey, removedIds: List<UUID>) {
        identity.switchKey(newCode)
        store.save(store.devices.value.map { ReceivedDevice(it.file, it.receivedAt, it.method, ByteArray(0)) })
        scan.reset()
        keyGeneration++
        val methods = rekeyMethods
        val deviceIds = removedIds + identity.deviceId
        backgroundScope?.launch(Dispatchers.IO) {
            for (method in methods) {
                runCatching { method.clearOldGroup(oldKey, deviceIds) }
            }
        }
    }

    private fun forwardRekey(bytes: ByteArray, cameBy: String, oldKey: com.accountabilibuddy.shared.sync.SyncKey) {
        val methods = rekeyMethods.filter { it.method != cameBy }
        val fileName = RekeyCodec.fileNameFor(oldKey.groupId)
        backgroundScope?.launch(Dispatchers.IO) {
            for (method in methods) {
                runCatching { method.publishRekey(bytes, fileName, oldKey) }
            }
        }
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
        // The rekey file (ADR 54) travels with the devices' files, under a name shaped like theirs.
        if (RekeyCodec.isRekey(bytes)) {
            return acceptRekey(bytes, method)
        }

        val file = SyncFileCodec.open(bytes, key) ?: return CANNOT_READ
        // A removed device's file is refused (ADR 54). One that opens with the group's NEW key, written after the removal, can
        // only come from a device that was linked again: it is a member once more.
        removedStore.devices.value.firstOrNull { it.deviceId == file.device.deviceId }?.let { removed ->
            if (!file.device.writtenAt.isAfter(removed.removedAt)) {
                return REMOVED_DEVICE
            }

            removedStore.clear(removed.deviceId)
        }

        val held = store.devices.value.firstOrNull { it.file.device.deviceId == file.device.deviceId }
        // The copy kept of the sender's file doesn't hold the shared lists (ADR 48): they merge into this phone's own sealed
        // store below, so their text isn't kept a second time in the clear. The sealed bytes it carries on are sealed.
        // An empty "parked" stays as the mark that the device keeps the shared lists (a QR file carries none: the mark is kept).
        val mark = if (file.parked != null || held?.file?.parked != null) emptyList<com.accountabilibuddy.shared.sync.SyncParked>() else null
        val outcome = SyncCarry.accept(store.devices.value, file.copy(nextTasks = null, parked = mark, later = null), bytes, method, identity.deviceId, now())
        if (outcome.error != null) {
            return outcome.error
        }

        store.save(outcome.devices)
        // Not from a device whose clock runs more than a minute ahead; a later file from it, once the clocks agree, is taken.
        if ((file.nextTasks != null || file.parked != null || file.later != null) && !file.device.writtenAt.isAfter(now().plusSeconds(60))) {
            // A file with a "later" list (even empty) comes from a writer that knows ADR 50's fields; one without, from an older app.
            onSharedLists(file.nextTasks, file.parked, file.later, file.later != null)
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
            nextAlarm().takeIf { settingsStore.settings.value.alarmOn }, identity.deviceKey.publicKeyBase64,
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
        later = runCatching { later() }.getOrNull(),
    )

    /** Everything to hand over by [method] (Drive or a share): this phone's own file first, then what it carries. */
    fun filesToCarry(method: String): List<ByteArray> =
        listOfNotNull(ownSealed()) + SyncCarry.filesFor(store.devices.value, method, emptyMap(), removedIds())

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
            .filter { it.method != cameBy && it.carryable && it.file.device.deviceId !in removedIds() }
            .filter { device -> inFolder[device.file.device.deviceId]?.let { device.file.device.writtenAt > it } ?: true }
            .map { FolderFile(SyncFolder.fileNameFor(key.groupId, it.file.device.deviceId), it.file.device.deviceId, it.file.device.writtenAt, it.sealed) }
        val ownFile = if (gate.due(own, now(), inFocusSession())) {
            listOf(FolderFile(SyncFolder.fileNameFor(key.groupId, identity.deviceId), identity.deviceId, own.device.writtenAt, SyncFileCodec.seal(own, key), own = true))
        } else {
            emptyList()
        }
        return ownFile + carried
    }

    // The devices removed from the group: their files are never carried on (the rekey file is, see acceptRekey).
    private fun removedIds(): Set<UUID> = removedStore.devices.value.map { it.deviceId }.toSet()

    private fun ownSealed(): ByteArray? = identity.key.value?.let { SyncFileCodec.seal(ownFile(), it) }

    companion object {
        const val NOT_JOINED = "This phone isn't linked to your other devices yet."
        const val CANNOT_READ = "This file can't be read: it's from another device group, or it's damaged."

        /** Why a file was refused when it is from a device removed from the group (ADR 54). */
        const val REMOVED_DEVICE = "This file is from a device that was removed from your device group."
    }
}
