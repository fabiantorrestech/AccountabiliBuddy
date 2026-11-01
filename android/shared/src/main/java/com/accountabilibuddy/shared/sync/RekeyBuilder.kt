package com.accountabilibuddy.shared.sync

import java.security.SecureRandom
import java.time.Instant
import java.util.UUID

/** A device that stays in the group when others are removed (ADR 54), with the public key its card published (standard base64). */
class RekeyRecipient(val deviceId: UUID, val publicKeyBase64: String)

/**
 * A rekey made by [RekeyBuilder] (ADR 54).
 *
 * @property sealedFile The rekey file's bytes, sealed with the old key; written to every method ([RekeyCodec.fileNameFor]).
 * @property newCode The new link code (40 characters); the maker switches to it and keeps it only through TokenVault.
 * @property payload What the file says.
 * @property notWrapped The remaining devices that got no wrap because they had no valid public key; they are cut off like the removed ones.
 */
class RekeyBuild(val sealedFile: ByteArray, val newCode: String, val payload: RekeyPayload, val notWrapped: List<UUID>)

/** Makes a rekey (ADR 54): a new link code, wrapped for every remaining device that has a public key. */
object RekeyBuilder {
    private val random = SecureRandom()

    /**
     * @param oldKey The group's key now; it seals the file, so the removed devices can read that they were removed.
     * @param maker This device (its id and name), named in the file so a removed device can say who removed it.
     * @param removed The devices to remove.
     * @param recipients The devices that stay (not the maker: it already has the new code).
     */
    fun build(oldKey: SyncKey, maker: RekeyRemovedDevice, removed: List<RekeyRemovedDevice>, recipients: List<RekeyRecipient>, now: Instant): RekeyBuild {
        val secret = ByteArray(SyncKey.SECRET_BYTES)
        random.nextBytes(secret)
        try {
            val wraps = ArrayList<RekeyWrapRecord>()
            val notWrapped = ArrayList<UUID>()
            for (recipient in recipients) {
                val publicKey = runCatching { java.util.Base64.getDecoder().decode(recipient.publicKeyBase64) }.getOrNull()
                val wrap = publicKey?.let { RekeyWrap.create(secret, recipient.deviceId, it, oldKey.groupId) }
                if (wrap == null) notWrapped += recipient.deviceId else wraps += wrap
            }

            val payload = RekeyPayload(now.epochSecond, maker, removed, wraps)
            return RekeyBuild(RekeyCodec.seal(payload, oldKey), SyncKeyCode.fromSecret(secret), payload, notWrapped)
        } finally {
            secret.fill(0)
        }
    }
}

/** What a rekey file means for the device that reads it (ADR 54). */
enum class RekeyReadingKind {
    /** The device is among the removed ones. */
    Removed,

    /** The device stays and unwrapped the new link code. */
    NewKey,

    /** The device isn't removed but has no wrap it can open (it had no public key when the rekey was made): it is cut off all the same. */
    CutOff,
}

/** A device's reading of a rekey (ADR 54): [newCode] is set only for [RekeyReadingKind.NewKey]. */
class RekeyReading(val kind: RekeyReadingKind, val newCode: String?)

/** Reads a rekey for this device (ADR 54): removed, given the new key, or cut off. */
object RekeyReader {
    fun read(payload: RekeyPayload, ownId: UUID, ownKey: SyncDeviceKey, oldGroupId: String): RekeyReading {
        if (payload.removed.any { it.deviceId == ownId }) {
            return RekeyReading(RekeyReadingKind.Removed, null)
        }

        for (wrap in payload.wraps.filter { it.deviceId == ownId }) {
            val secret = RekeyWrap.tryUnwrap(wrap, ownKey, oldGroupId) ?: continue
            try {
                return RekeyReading(RekeyReadingKind.NewKey, SyncKeyCode.fromSecret(secret))
            } finally {
                secret.fill(0)
            }
        }

        return RekeyReading(RekeyReadingKind.CutOff, null)
    }
}
