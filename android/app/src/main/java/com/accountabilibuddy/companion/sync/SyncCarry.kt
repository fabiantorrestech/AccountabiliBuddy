package com.accountabilibuddy.companion.sync

import com.accountabilibuddy.shared.sync.SyncFile
import com.accountabilibuddy.shared.sync.SyncMerge
import java.time.Instant
import java.util.UUID

/**
 * The phone as a carrier (ADR 22), with no Android in it: taking a received file into its copies, what the phone has
 * seen from each device, and which files go where. Files that came by QR code or a share go to the PCs through
 * Drive; the PC's files go to Drive or out as a share; a file never goes back the way it came.
 */
object SyncCarry {
    /** The copies after a file was taken in, or the copies unchanged and why not. */
    class Outcome(val devices: List<ReceivedDevice>, val error: String?)

    /**
     * Merges [file] into the copy of its device ([SyncMerge.merge]); a file older than the copy, or the phone's own,
     * changes nothing.
     *
     * @param sealed The file's bytes as they arrived, kept for carrying on.
     * @param method How it came, see [ReceivedDevice.method].
     */
    fun accept(devices: List<ReceivedDevice>, file: SyncFile, sealed: ByteArray, method: String, ownId: UUID, now: Instant): Outcome {
        val sender = file.device
        if (sender.deviceId == ownId) {
            return Outcome(devices, "This is this phone's own file.")
        }

        val stored = devices.firstOrNull { it.file.device.deviceId == sender.deviceId }
        if (stored != null && sender.writtenAt < stored.file.device.writtenAt) {
            return Outcome(devices, "This phone already has newer data from ${sender.name}.")
        }

        val merged = SyncMerge.merge(stored?.file, file, now)
        return Outcome(devices.filter { it !== stored } + ReceivedDevice(merged, now, method, sealed), null)
    }

    /** For each device, the newest file the phone holds from it (the card's "seen"). */
    fun seen(devices: List<ReceivedDevice>): Map<UUID, Instant> =
        devices.associate { it.file.device.deviceId to it.file.device.writtenAt }

    /**
     * The sealed files to hand over by [method]: not the ones that came that way (back to where they came from), nor
     * the ones the recipient already has, by its [recipientSeen].
     */
    fun filesFor(devices: List<ReceivedDevice>, method: String, recipientSeen: Map<UUID, Instant>): List<ByteArray> =
        devices
            .filter { it.method != method }
            .filter { device -> recipientSeen[device.file.device.deviceId]?.let { device.file.device.writtenAt > it } ?: true }
            .map { it.sealed }
}
