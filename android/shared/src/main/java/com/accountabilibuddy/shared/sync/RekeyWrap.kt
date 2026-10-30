package com.accountabilibuddy.shared.sync

import java.nio.ByteBuffer
import java.security.GeneralSecurityException
import java.security.SecureRandom
import java.util.UUID
import javax.crypto.Cipher
import javax.crypto.Mac
import javax.crypto.spec.GCMParameterSpec
import javax.crypto.spec.SecretKeySpec

/**
 * The new link code's 25-byte secret wrapped for one device (ADR 54), as a rekey file holds it.
 *
 * @property deviceId The device that can unwrap it.
 * @property ephemeralPublicKey The 65-byte public key of the one-time key pair the wrapper used ([SyncDeviceKey]).
 * @property nonce The AES-GCM nonce (12 bytes).
 * @property wrapped The 25-byte secret encrypted, then the 16-byte tag (41 bytes).
 */
class RekeyWrapRecord(val deviceId: UUID, val ephemeralPublicKey: ByteArray, val nonce: ByteArray, val wrapped: ByteArray)

/**
 * Wraps the new link code's 25-byte secret for one device (ADR 54), so only that device can read it. The PC does the same
 * (Core's RekeyWrap) and the two must stay byte for byte alike:
 * 1. The wrapper makes a one-time ECDH P-256 key pair and takes the ECDH shared secret with the recipient's public key (the
 *    X coordinate, 32 bytes).
 * 2. The AES-256-GCM key is HKDF-SHA256 over that secret, salt "AccountabiliBuddy rekey" (UTF-8), info = "wrap" (ASCII) + the
 *    recipient's 16 id bytes + the 4 bytes of the old group id; 32 bytes.
 * 3. The secret is encrypted with a random 12-byte nonce; the associated data is the same info bytes; the 16-byte tag follows
 *    the ciphertext (41 bytes in all).
 * A device id's 16 bytes are the UUID in RFC 4122 order (the order of its text).
 */
object RekeyWrap {
    /** The length of [RekeyWrapRecord.wrapped]: the secret and the tag. */
    const val WRAPPED_BYTES = SyncKey.SECRET_BYTES + 16

    /** The length of [RekeyWrapRecord.nonce]. */
    const val NONCE_BYTES = 12

    private const val TAG_BITS = 128
    private const val KEY_BYTES = 32
    private const val TRANSFORMATION = "AES/GCM/NoPadding"
    private val salt = "AccountabiliBuddy rekey".toByteArray(Charsets.UTF_8)
    private val infoPrefix = "wrap".toByteArray(Charsets.US_ASCII)
    private val random = SecureRandom()

    /**
     * The [secret] wrapped for the recipient; null when its public key isn't a valid P-256 point.
     *
     * @param oldGroupId The group id (8 hex characters) of the key the rekey file is sealed with.
     */
    fun create(secret: ByteArray, recipientId: UUID, recipientPublicKey: ByteArray, oldGroupId: String): RekeyWrapRecord? {
        require(secret.size == SyncKey.SECRET_BYTES) { "The secret is ${SyncKey.SECRET_BYTES} bytes." }
        val ephemeral = SyncDeviceKey.generate()
        val shared = ephemeral.deriveSecret(recipientPublicKey) ?: return null
        val info = infoFor(recipientId, oldGroupId)
        val key = deriveKey(shared, info)
        try {
            val nonce = ByteArray(NONCE_BYTES)
            random.nextBytes(nonce)
            val cipher = Cipher.getInstance(TRANSFORMATION)
            cipher.init(Cipher.ENCRYPT_MODE, SecretKeySpec(key, "AES"), GCMParameterSpec(TAG_BITS, nonce))
            cipher.updateAAD(info)
            return RekeyWrapRecord(recipientId, ephemeral.publicKey, nonce, cipher.doFinal(secret))
        } finally {
            shared.fill(0)
            key.fill(0)
        }
    }

    /** The 25-byte secret; null when the record is malformed or wasn't wrapped for this device's key and group. */
    fun tryUnwrap(record: RekeyWrapRecord, ownKey: SyncDeviceKey, oldGroupId: String): ByteArray? {
        if (record.nonce.size != NONCE_BYTES || record.wrapped.size != WRAPPED_BYTES) {
            return null
        }

        val shared = ownKey.deriveSecret(record.ephemeralPublicKey) ?: return null
        val info = infoFor(record.deviceId, oldGroupId)
        val key = deriveKey(shared, info)
        try {
            val cipher = Cipher.getInstance(TRANSFORMATION)
            cipher.init(Cipher.DECRYPT_MODE, SecretKeySpec(key, "AES"), GCMParameterSpec(TAG_BITS, record.nonce))
            cipher.updateAAD(info)
            return cipher.doFinal(record.wrapped)
        } catch (e: GeneralSecurityException) {
            return null
        } finally {
            shared.fill(0)
            key.fill(0)
        }
    }

    /** A device id as the 16 bytes a rekey file holds. */
    internal fun idBytes(id: UUID): ByteArray =
        ByteBuffer.allocate(16).putLong(id.mostSignificantBits).putLong(id.leastSignificantBits).array()

    internal fun idOf(bytes: ByteArray, offset: Int): UUID {
        val buffer = ByteBuffer.wrap(bytes, offset, 16)
        return UUID(buffer.long, buffer.long)
    }

    private fun infoFor(recipientId: UUID, oldGroupId: String): ByteArray {
        val groupBytes = ByteArray(4) { oldGroupId.substring(it * 2, it * 2 + 2).toInt(16).toByte() }
        return infoPrefix + idBytes(recipientId) + groupBytes
    }

    // HKDF-SHA256 (RFC 5869): extract with the salt, then expand to 32 bytes (one block).
    private fun deriveKey(shared: ByteArray, info: ByteArray): ByteArray {
        val mac = Mac.getInstance("HmacSHA256")
        mac.init(SecretKeySpec(salt, "HmacSHA256"))
        val pseudoRandomKey = mac.doFinal(shared)
        mac.init(SecretKeySpec(pseudoRandomKey, "HmacSHA256"))
        mac.update(info)
        mac.update(1.toByte())
        pseudoRandomKey.fill(0)
        return mac.doFinal().copyOf(KEY_BYTES)
    }
}
