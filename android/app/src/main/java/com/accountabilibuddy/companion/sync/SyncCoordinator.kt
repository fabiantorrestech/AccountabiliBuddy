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
    private val pastKeys: PastKeysStore,
    private val deviceName: String,
    private val appVersion: String,
    private val settingsStore: PhoneSyncSettingsStore,
    private val now: () -> Instant = Instant::now,
) {
    private val scan = QrScan()

    init {
        // The live list follows the removed list (ADR 54).
        removedStore.onChanged = { store.refreshActive() }
        // A phone that is in a group is not unlinked (the file was left over from a race or a restore).
        if (identity.key.value != null && removedStore.unlinked.value != null) {
            removedStore.setUnlinked(null)
        }
    }

    /** The other devices' parts on this phone. */
    val received: StateFlow<List<ReceivedDevice>> = store.active

    /** Every device's copy held, the removed ones included (history only, ADR 54); [received] has only the devices still in the group. */
    val allReceived: StateFlow<List<ReceivedDevice>> = store.devices

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

    /** The wait for turning off the last way of syncing (prefs "syncOff"); set by the app. Without it a turn-off applies at once. */
    var offWait: SyncOffWait? = null

    /** Whether a way of syncing reports a problem right now (it can't open its folder, reach its relay, ...); set by the app. */
    var hasProblem: (SyncMethod) -> Boolean = { false }

    /**
     * Saves the sharing settings. Null when saved as asked; otherwise a line for the user: why not, or when a held turn-off happens.
     * - During a focus session the user started a way of syncing may be turned on but never off or pointed somewhere else
     *   ([PhoneSyncSettings.narrowedBy], [removalLockedReason]), so a frozen session can still be recovered.
     * - Between sessions the ways that count ([SyncOffWait.counted]: shared folder, Drive, own relay, on and unchanged for 10 minutes) are
     *   guarded the same way. Turning off the last one that counts is a looser change: it is held for [SyncOffWait.WAIT] with the way still on
     *   and applied by [applyPendingSyncOff]. Pointing the last one somewhere else is refused, unless it reports a problem (so a broken one can
     *   be fixed). Turning off one of several, turning a way on, QR codes, Sync files and every other field apply at once.
     */
    @Synchronized
    fun saveSettings(settings: PhoneSyncSettings): String? {
        val current = settingsStore.settings.value
        if (current.narrowedBy(settings)) {
            removalLockedReason()?.let { return it }
        }

        var toSave = settings
        var note: String? = null
        val wait = offWait
        if (wait != null) {
            val time = now()
            val counted = wait.countedWays(current, time)
            val stays = counted.any { it.isLive(settings) && it.sameIn(current, settings) }
            if (counted.isNotEmpty() && !stays) {
                val repointed = counted.firstOrNull { it.isLive(settings) && !hasProblem(it) }
                if (repointed != null) {
                    return "${repointed.label} is your only way of syncing, so it can't be pointed somewhere else now. " +
                        "Turn another way on first; it counts after ${SyncOffWait.WAIT.toMinutes()} minutes."
                }

                val turnedOff = counted.filter { !it.isLive(settings) }
                if (turnedOff.isNotEmpty()) {
                    val pending = wait.start(wait.pending.value?.method?.takeIf { it in turnedOff } ?: turnedOff.last(), time)
                    toSave = pending.method.keptOn(toSave, current)
                    note = "${pending.method.label} turns off at ${CLOCK.format(pending.at.atZone(java.time.ZoneId.systemDefault()))}"
                }
            }
        }

        settingsStore.save(toSave)
        wait?.observe(toSave, now())
        return note
    }

    /** Whether turning [method] off now would wait: it is the only way that counts. The Devices page asks before it asks the user. */
    fun turnOffWaits(method: SyncMethod): Boolean {
        val wait = offWait ?: return false
        return wait.countedWays(settingsStore.settings.value, now()) == listOf(method)
    }

    /** Whether this file's settings would leave the ways that count without one (an import must not do that; see [saveSettings]). */
    fun leavesNoWay(next: PhoneSyncSettings): Boolean {
        val wait = offWait ?: return false
        val current = settingsStore.settings.value
        val counted = wait.countedWays(current, now())
        return counted.isNotEmpty() && counted.none { it.isLive(next) && it.sameIn(current, next) }
    }

    /** Drops a held turn-off: the way simply stays on. Allowed at any time. */
    fun cancelPendingSyncOff() {
        offWait?.cancel()
    }

    /**
     * Turns off the way whose wait is over, once no focus session runs (a wait that ends in a session applies after it). Nothing else
     * applies it earlier. True when something was applied.
     */
    @Synchronized
    fun applyPendingSyncOff(time: Instant = now()): Boolean {
        val wait = offWait ?: return false
        val pending = wait.pending.value ?: return false
        val current = settingsStore.settings.value
        if (!pending.method.isLive(current)) {
            wait.cancel()
            return false
        }

        if (time.isBefore(pending.at) || removalLockedReason() != null) {
            return false
        }

        val next = pending.method.turnedOff(current)
        settingsStore.save(next)
        wait.observe(next, time)
        wait.cancel()
        return true
    }

    /** The group's link code (to show so another device joins); null outside a group. */
    fun linkCode(): String? = identity.code()

    /** This phone's own file as QR code links, for "Show as picture" (a PC without a camera reads a screenshot). */
    fun ownQrLinks(): List<String> {
        val key = identity.key.value ?: return emptyList()
        // The shared lists with their tombstones (ADR 48) stay out of QR codes, as on the PC: a file without them leaves them alone.
        return SyncQr.links(SyncFileCodec.seal(ownFile().copy(nextTasks = null, parked = null, later = null, pastKeys = null, categories = null), key), key.groupId)
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
     * This phone's own task categories, name and colour, for its file so the PCs can offer them. The phone keeps no category list of its
     * own yet (its picker lists the categories it has seen on tasks), so this stays null until the phone gets one; the field and its JSON
     * are in place.
     */
    var categories: () -> List<com.accountabilibuddy.shared.sync.SyncCategory>? = { null }

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
 * False too when it would move to another group during a focus session the user started ([removalLockedReason]).
     */
    fun join(code: String): Boolean {
        val current = identity.code()
        if (current != null && SyncKeyCode.parse(current)?.sameGroupAs(SyncKeyCode.parse(code)) == true) {
            return SyncKeyCode.parse(code) != null
        }

        // Moving to another group drops the PC's copy mid-session; locked like leave() (no escape hatches).
        if (current != null && removalLockedReason() != null) {
            return false
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

    /**
     * Leaves the group and forgets every device's part on this phone (ADR 15).
     *
     * @return Null when done; otherwise why not (a focus session the user started).
     */
    fun leave(): String? {
        removalLockedReason()?.let { return it }
        identity.leave()
        store.clear()
        // Leaving the group forgets its past keys too (ADR 56); continuing unsynced and joining again keep them.
        pastKeys.clear()
        scan.reset()
        keyGeneration++
        return null
    }

    /**
     * "Continue unsynced here" (ADR 54): this phone stays out of the group and keeps its own data, and what it had received
     * from the group's devices stays as it was at the last sync (shown as not syncing); a later join forgets it, as any join
     * does. The removed list stays too, so removed devices keep their label.
     *
     * @return Null when done; otherwise why not (a focus session the user started).
     */
    fun continueUnsynced(): String? {
        removalLockedReason()?.let { return it }
        removedStore.setUnlinked(null)
        identity.leave()
        scan.reset()
        keyGeneration++
        return null
    }

    /**
     * Applies the user's choice for a removed device's received data (ADR 55): [RemovedDevice.ERASE_DATA] forgets what this
     * phone received from it; [RemovedDevice.KEEP_DATA] keeps it, shown as removed. The choice is recorded either way.
     * Refused during a focus session the user started (erasing a PC's copy would end the phone-down guard).
     *
     * @return Null when done; otherwise why not.
     */
    @Synchronized
    fun applyDataChoice(deviceId: UUID, choice: String): String? {
        removalLockedReason()?.let { return it }
        if (choice == RemovedDevice.ERASE_DATA) {
            store.save(store.devices.value.filter { it.file.device.deviceId != deviceId })
        }

        removedStore.setDataChoice(deviceId, choice)
        return null
    }

    /** This phone's sync device id; it stays when the phone leaves a group. */
    val deviceId: UUID
        get() = identity.deviceId

    /**
     * The names of the devices that would be cut off along with [deviceIds] because they can't receive the new key (ADR 54): an app
     * from before ADR 54 (no public key in its card), and devices this phone only knows from the others' "seen" lists (no name).
     * The remove flow names them before the user confirms.
     */
    fun notWrappedFor(deviceIds: List<UUID>): List<String> {
        val cards = store.devices.value.map { it.file.device }
        val known = cards.map { it.deviceId }.toSet()
        val isRemoved = { id: UUID -> removedStore.devices.value.any { it.deviceId == id } }
        val oldApps = cards.filter { it.deviceId !in deviceIds && !isRemoved(it.deviceId) && it.publicKey.isNullOrEmpty() }.map { it.name }
        val unknown = cards.flatMap { it.seen.keys }
            .filter { it != identity.deviceId && it !in known && it !in deviceIds && !isRemoved(it) }
            .distinct()
        // Devices only named in the others' lists have no name: one line with a count, not the same line over and over.
        val unnamed = when (unknown.size) {
            0 -> emptyList()
            1 -> listOf("A device this phone hasn't received a file from")
            else -> listOf("${unknown.size} devices this phone hasn't received a file from")
        }
        return oldApps + unnamed
    }

    /**
     * Brings a removed device's backup ([file], opened by [BackupOpener]) in as a received device under its old name and records it as
     * removed, so it shows as "[name] (removed)" (ADR 55). Not carried on: the copy has no sealed bytes. A device that is linked and
     * sending here already with newer data (it was removed and added again) is left as it is.
     *
     * @return Null when restored; otherwise why not.
     */
    @Synchronized
    fun restoreBackup(file: SyncFile): String? {
        val id = file.device.deviceId
        val held = store.devices.value.firstOrNull { it.file.device.deviceId == id }
        val removed = removedStore.devices.value.any { it.deviceId == id }
        if (held != null && !removed && held.file.device.writtenAt >= file.device.writtenAt) {
            return "${file.device.name} is linked to your group again and this phone already has newer data from it."
        }

        val outcome = SyncCarry.accept(store.devices.value, file.copy(pastKeys = null), ByteArray(0), SyncDeviceCard.METHOD_FILE, identity.deviceId, now())
        if (outcome.error != null) {
            return outcome.error
        }

        return try {
            // Recorded first, so the restored device never shows up among the live ones (everything live reads the active list).
            if (held == null || removed) {
                removedStore.recordRestored(id, file.device.name, file.device.writtenAt)
            }

            store.save(outcome.devices)
            null
        } catch (e: java.io.IOException) {
            "Couldn't save the restored data right now. Try again."
        }
    }

    /**
     * Removes [deviceIds] from the group (ADR 54): makes a new link code, writes this phone's rekey file to every sync method in
     * use, then switches this phone to the new key, keeping everything it received. Their received data is left as it is;
     * [applyDataChoice] is the caller's next step. Nothing changes when it can't be done, or when no method took the file
     * (the other devices would never hear of it).
     *
     * Two devices removing at the same time would split the group, so: a rekey another device already wrote for this group is
     * applied here instead and the call returns a [RemovalResult.conflict] (look and try again), and after writing, a rekey from
     * a device with a lower id (the one every device picks, [RekeyCodec.compareMakers]) is applied instead of this one.
     */
    suspend fun removeDevices(deviceIds: List<UUID>, erase: Boolean = false): RemovalResult {
        removalLockedReason()?.let { return RemovalResult.failed(it) }
        val oldKey = identity.key.value ?: return RemovalResult.failed(NOT_JOINED)
        val ids = deviceIds.distinct()
        if (ids.isEmpty() || identity.deviceId in ids) {
            return RemovalResult.failed("Pick other devices to remove; this phone leaves the group with Leave group.")
        }

        // Removing one twice (a dialog opened twice, or another device just removed it) would re-key again for nothing.
        if (ids.any { id -> removedStore.devices.value.any { it.deviceId == id } }) {
            return RemovalResult.failed("That device is already removed from your group.")
        }

        val cards = store.devices.value.map { it.file.device }
        // The erase choice goes into the rekey file: every staying device that applies it forgets these devices' received data (ADR 55).
        val removing = ids.map { id -> RekeyRemovedDevice(id, cards.firstOrNull { it.deviceId == id }?.name ?: "Unknown device", erase) }
        val isRemoved = { id: UUID -> removedStore.devices.value.any { it.deviceId == id } }
        val staying = cards.filter { it.deviceId !in ids && !isRemoved(it.deviceId) }
        // This phone is among the recipients, so reading its own file (after a crash between writing and switching) gives it the
        // new key instead of "unlinked".
        val recipients = staying.mapNotNull { card -> card.publicKey?.takeIf { it.isNotEmpty() }?.let { RekeyRecipient(card.deviceId, it) } } +
            RekeyRecipient(identity.deviceId, identity.deviceKey.publicKeyBase64)
        val build = RekeyBuilder.build(oldKey, RekeyRemovedDevice(identity.deviceId, deviceName), removing, recipients, now())

        // The devices that stay but can't get the new key: an app from before ADR 54 (no public key in its card), a key that
        // isn't a P-256 point, or a device this phone never received a file from (only named in the others' "seen" lists). They
        // are cut off like the removed ones; the remove dialog names them.
        val known = cards.map { it.deviceId }.toSet()
        val seenIds = cards.flatMap { it.seen.keys }
        val unknown = seenIds.filter { it != identity.deviceId && it !in known && it !in ids && !isRemoved(it) }
        val notWrapped = (staying.filter { it.publicKey.isNullOrEmpty() }.map { it.deviceId } + build.notWrapped + unknown).distinct()

        // Where another device's rekey for this group would be, if there is one.
        val otherNames = (known + seenIds).filter { it != identity.deviceId }.distinct().map { RekeyCodec.fileNameFor(oldKey.groupId, it) }.toSet()
        if (applyOthersRekey(oldKey, otherNames, lowerThanThisPhoneOnly = false)) {
            return RemovalResult.conflictWith("Another device just changed the device group. Its change was applied here; check your devices and try again.")
        }

        val fileName = RekeyCodec.fileNameFor(oldKey.groupId, identity.deviceId)
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

        // Written first, then looked for: of two devices that wrote at the same time, the one with the higher id sees the other's file.
        if (applyOthersRekey(oldKey, otherNames, lowerThanThisPhoneOnly = true)) {
            return RemovalResult.conflictWith("Another device changed the device group at the same time. Its change was applied here instead; check your devices and try again.")
        }

        synchronized(this) {
            // This phone's own file, read meanwhile, already switched it to the new key (it is among the recipients): done.
            if (SyncKeyCode.parse(build.newCode)?.sameGroupAs(identity.key.value) == true) {
                return RemovalResult(true, null, notWrapped, failed)
            }

            // Another rekey switched or unlinked the phone while the file was being written.
            if (identity.key.value?.sameGroupAs(oldKey) != true) {
                return RemovalResult.conflictWith("The device group changed while this was being saved. Check your devices and try again.")
            }

            removedStore.record(removing.map { it.deviceId to it.name }, java.time.Instant.ofEpochSecond(build.payload.epoch))
            switchTo(build.newCode, oldKey, removing.map { it.deviceId })
        }

        return RemovalResult(true, null, notWrapped, failed)
    }

    // A rekey for this phone's group that came in, waiting for the others of the same read pass.
    private class PendingRekey(val payload: com.accountabilibuddy.shared.sync.RekeyPayload, val bytes: ByteArray, val key: SyncKey, val cameBy: String)

    // Rekeys for the current group that wait for the others of the same read pass, one per maker (see acceptRekey).
    private val pendingRekeys = mutableListOf<PendingRekey>()
    private var applyScheduled = false

    /**
     * Takes in a rekey file (ADR 54) another device wrote, from [method]. A rekey for a group this phone has already left or
     * switched away from is ignored, which also makes a repeated file harmless. A rekey for this phone's group is kept for a few
     * seconds before it is applied: if two devices removed at the same time, the one with the lower id wins
     * ([RekeyCodec.compareMakers]), whichever file arrived first. When the chosen rekey names this phone (or leaves it out) this
     * phone becomes [unlinked]: the key is dropped, so nothing syncs by any method, and what was received stays.
     * Null when taken in or ignored; otherwise why not.
     */
    private fun acceptRekey(bytes: ByteArray, method: String): String? {
        val current = identity.key.value ?: return NOT_JOINED
        if (RekeyCodec.groupIdOf(bytes) != current.groupId) {
            return null
        }

        val payload = RekeyCodec.open(bytes, current) ?: return CANNOT_READ
        // One per maker: a newer file from the same maker replaces its older one.
        val maker = payload.removedBy.deviceId
        pendingRekeys.removeAll { it.payload.removedBy.deviceId == maker && it.payload.epoch <= payload.epoch }
        if (pendingRekeys.none { it.payload.removedBy.deviceId == maker }) {
            pendingRekeys += PendingRekey(payload, bytes, current, method)
        }

        val scope = backgroundScope
        if (scope == null) {
            applyPendingRekeys()
        } else if (!applyScheduled) {
            applyScheduled = true
            scope.launch {
                kotlinx.coroutines.delay(PICK_WINDOW_MS)
                applyPendingRekeys()
            }
        }

        return null
    }

    // Applies the one rekey every device picks among those that came in. If it can't be kept right now (storage) the rekeys are
    // put back and tried again later: the sources have already marked their files as seen, so nothing else would bring them back.
    @Synchronized
    private fun applyPendingRekeys() {
        applyScheduled = false
        // Only rekeys for the group this phone is in now: one for a group it already left (it was buffered while another apply
        // ran) must not win the pick and take the valid one down with it.
        val current = identity.key.value
        val candidates = pendingRekeys.filter { current?.sameGroupAs(it.key) == true }
        pendingRekeys.clear()
        val chosen = candidates.minWithOrNull { a, b -> RekeyCodec.compareMakers(a.payload.removedBy.deviceId, b.payload.removedBy.deviceId) } ?: return
        try {
            applyRekey(chosen)
        } catch (e: java.io.IOException) {
            pendingRekeys.addAll(candidates)
            val scope = backgroundScope
            if (scope != null && !applyScheduled) {
                applyScheduled = true
                scope.launch {
                    kotlinx.coroutines.delay(RETRY_MS)
                    applyPendingRekeys()
                }
            }
        }
    }

    // Applies the best rekey among files other devices wrote for oldKey's group (the lowest maker id, and only those below this
    // phone's when asked). False when there is none.
    private suspend fun applyOthersRekey(oldKey: SyncKey, fileNames: Set<String>, lowerThanThisPhoneOnly: Boolean): Boolean {
        var best: PendingRekey? = null
        for (method in rekeyMethods) {
            for (bytes in method.findRekeys(fileNames, oldKey)) {
                if (RekeyCodec.groupIdOf(bytes) != oldKey.groupId) {
                    continue
                }

                val payload = RekeyCodec.open(bytes, oldKey) ?: continue
                val maker = payload.removedBy.deviceId
                if (maker == identity.deviceId || (lowerThanThisPhoneOnly && RekeyCodec.compareMakers(maker, identity.deviceId) > 0)) {
                    continue
                }

                if (best == null || RekeyCodec.compareMakers(maker, best.payload.removedBy.deviceId) < 0) {
                    best = PendingRekey(payload, bytes, oldKey, "")
                }
            }
        }

        val chosen = best ?: return false
        synchronized(this) { applyRekey(chosen) }
        return true
    }

    // Applies one rekey: switch to the new key, or become unlinked when it names this phone or leaves it out.
    private fun applyRekey(rekey: PendingRekey) {
        val current = identity.key.value
        // Another file already switched (or unlinked) this phone meanwhile.
        if (current == null || !current.sameGroupAs(rekey.key)) {
            return
        }

        val payload = rekey.payload
        val reading = RekeyReader.read(payload, identity.deviceId, identity.deviceKey, current.groupId)
        // When the removal happened, by its maker's clock, not when this phone learned of it: files a device wrote while this
        // phone was away are judged against it.
        // A device that stays also takes the remover's choice about the removed devices' data (ADR 55): erase is recorded and done
        // below (or at the next file once a focus session has ended). One that is removed itself keeps everything it has.
        val stays = reading.kind == RekeyReadingKind.NewKey
        val others = payload.removed.filter { it.deviceId != identity.deviceId }
        // This phone's own rekey file, read back (a folder or Drive poll finds it): the maker's erase waits for its backup and is applied
        // by applyDataChoice, as on the PC. Recording the choice here would erase its copy before the backup is made or given up.
        val madeHere = payload.removedBy.deviceId == identity.deviceId
        removedStore.record(
            others.map { it.deviceId to it.name },
            java.time.Instant.ofEpochSecond(payload.epoch),
            if (stays && !madeHere) others.associate { it.deviceId to if (it.erase) RemovedDevice.ERASE_DATA else RemovedDevice.KEEP_DATA } else emptyMap(),
        )
        val newCode = reading.newCode
        if (reading.kind == RekeyReadingKind.NewKey && newCode != null) {
            // Hand the file on to the methods it didn't come by, in the old group where the devices that haven't switched yet
            // still listen (the PCs that reach only one of them).
            forwardRekey(rekey.bytes, rekey.cameBy, current, payload.removedBy.deviceId)
            switchTo(newCode, current, payload.removed.map { it.deviceId })
            if (!madeHere) {
                applyPendingErases()
            }
        } else {
            // Removed, or left out of the rekey: there is no new key to switch to.
            removedStore.setUnlinked(UnlinkedState(now(), reading.kind == RekeyReadingKind.Removed, payload.removedBy.name))
            identity.leave()
            scan.reset()
            keyGeneration++
        }
    }

    // Switches to the new link code in place: what was received stays, but its sealed bytes can't be opened by the others any
    // more, so those copies aren't carried until the device's next file replaces them. The old group is dead: this phone's file,
    // the removed devices' and every device's old copy this phone carried are deleted in the background.
    private fun switchTo(newCode: String, oldKey: SyncKey, removedIds: List<UUID>) {
        val deviceIds = (removedIds + identity.deviceId + store.devices.value.map { it.file.device.deviceId }).distinct()
        identity.switchKey(newCode)
        // ADR 56: the group's outgoing key is kept (never the code), to open backups sealed with it.
        pastKeys.noteSwitched(oldKey)
        removedStore.setUnlinked(null)
        store.save(store.devices.value.map { ReceivedDevice(it.file, it.receivedAt, it.method, ByteArray(0)) })
        scan.reset()
        keyGeneration++
        val methods = rekeyMethods
        backgroundScope?.launch(Dispatchers.IO) {
            for (method in methods) {
                runCatching { method.clearOldGroup(oldKey, deviceIds) }
            }
        }
    }

    private fun forwardRekey(bytes: ByteArray, cameBy: String, oldKey: SyncKey, makerId: UUID) {
        val methods = rekeyMethods.filter { it.method != cameBy }
        val fileName = RekeyCodec.fileNameFor(oldKey.groupId, makerId)
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
            method == SyncDeviceCard.METHOD_FILE && !settings.fileOn -> return "Turn on \"Sync files\" in Settings › Devices & sync to open sync files."
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
        // The past keys (ADR 56) go into their own sealed store and stay out of the copy too.
        val mark = if (file.parked != null || held?.file?.parked != null) emptyList<com.accountabilibuddy.shared.sync.SyncParked>() else null
        val outcome = SyncCarry.accept(store.devices.value, file.copy(nextTasks = null, parked = mark, later = null, pastKeys = null), bytes, method, identity.deviceId, now())
        if (outcome.error != null) {
            return outcome.error
        }

        store.save(outcome.devices)
        pastKeys.merge(file.pastKeys)
        // An erase that waited for a focus session to end (ADR 55) is done at the next file after it.
        applyPendingErases()
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
            return "Turn on \"QR codes\" in Settings › Devices & sync to scan the PC's codes."
        }

        val key = identity.key.value ?: return "Join your devices first: scan the PC's link code (Settings › Devices & sync on the PC)."
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
            identity.deviceId, deviceName, SyncDeviceCard.KIND_PHONE, appVersion, now(), methods(), false, SyncCarry.seen(store.active.value), companions(),
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
        // ADR 56: the past keys, for opening backups; sealed with the current key like everything else.
        pastKeys = pastKeys.forFile(),
        categories = runCatching { categories() }.getOrNull(),
    )

    /**
     * Forgets what this phone received from every removed device whose recorded choice is erase (ADR 55), when it still holds a copy.
     * A staying device records the remover's choice from the rekey and erases here; it waits while a focus session the user started
     * runs ([removalLockedReason]: a running session's pickup evidence is not dropped) and runs again at the next file after it.
     */
    @Synchronized
    fun applyPendingErases() {
        val erase = removedStore.devices.value.filter { it.dataChoice == RemovedDevice.ERASE_DATA }.map { it.deviceId }.toSet()
        if (erase.isEmpty() || store.devices.value.none { it.file.device.deviceId in erase }) {
            return
        }

        if (removalLockedReason() != null) {
            // A session is running: look again in a minute (only while an erase is waiting, so nothing polls otherwise), not just at the next file.
            val scope = backgroundScope
            if (scope != null && !eraseRetryScheduled) {
                eraseRetryScheduled = true
                scope.launch {
                    kotlinx.coroutines.delay(ERASE_RETRY_MS)
                    eraseRetryScheduled = false
                    applyPendingErases()
                }
            }

            return
        }

        store.save(store.devices.value.filter { it.file.device.deviceId !in erase })
    }

    private var eraseRetryScheduled = false

    /** Everything to hand over by [method] (Drive or a share): this phone's own file first, then what it carries. */
    fun filesToCarry(method: String): List<ByteArray> =
        if (identity.key.value == null) emptyList() else listOfNotNull(ownSealed()) + SyncCarry.filesFor(store.devices.value, method, emptyMap(), removedIds())

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
        // How long a rekey waits for the others of the same read pass before the lowest maker's is applied.
        private const val PICK_WINDOW_MS = 3_000L

        // How long a deferred erase waits before it looks again while a PC focus session is running.
        private const val ERASE_RETRY_MS = 60_000L

        // How long rekeys wait to be tried again when an apply could not be kept.
        private const val RETRY_MS = 30_000L

        const val REMOVED_DEVICE = "This file is from a device that was removed from your device group."
    }
}

// "4:05 PM": 12-hour whatever the system says.
private val CLOCK: java.time.format.DateTimeFormatter = java.time.format.DateTimeFormatter.ofPattern("h:mm a", java.util.Locale.US)
