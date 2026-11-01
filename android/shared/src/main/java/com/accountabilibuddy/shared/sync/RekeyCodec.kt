package com.accountabilibuddy.shared.sync

import java.io.ByteArrayOutputStream
import java.nio.ByteBuffer
import java.security.GeneralSecurityException
import java.security.MessageDigest
import java.security.SecureRandom
import java.util.UUID
import javax.crypto.Cipher
import javax.crypto.spec.GCMParameterSpec
import javax.crypto.spec.SecretKeySpec

/**
 * A device a rekey file names (ADR 54): its id and the name it had. For a removed device, [erase] is the remover's choice about
 * its data (ADR 55): true = every staying device that applies the rekey forgets what it received from it; false = they keep it,
 * shown as removed. Always false for the maker entry, which is written without the flag.
 */
class RekeyRemovedDevice(val deviceId: UUID, val name: String, val erase: Boolean = false)

/**
 * What a rekey file says (ADR 54), once opened with the old group key.
 *
 * @property epoch When the rekey was made, in seconds since 1970-01-01 UTC. Tells rekeys apart; never compared across devices' clocks to refuse one.
 * @property removedBy The device that made the rekey (its id and name), so a removed device can say who removed it.
 * @property removed The devices this rekey removes from the group.
 * @property wraps The new link code's secret wrapped for each remaining device that had a public key.
 */
class RekeyPayload(val epoch: Long, val removedBy: RekeyRemovedDevice, val removed: List<RekeyRemovedDevice>, val wraps: List<RekeyWrapRecord>)

/**
 * Seals and opens the rekey file (ADR 54): what a device writes when it removes devices from the group. The PC writes and
 * reads the same bytes (Core's RekeyCodec); keep the two identical.
 *
 * ```
 * file    = "ABR1" (4 ASCII bytes)
 *           old group id (4 bytes, the group id of the key that seals it, as in "ABS1" files)
 *           nonce (12 random bytes)
 *           ciphertext (AES-256-GCM with the OLD group key; the associated data is the first 8 bytes of the file)
 *           tag (16 bytes)
 * payload = version (1 byte, 1)
 *           epoch (8 bytes, signed, big-endian: seconds since 1970-01-01 UTC)
 *           remover: id (16 bytes, RFC 4122 order), name length (2 bytes, big-endian), name (UTF-8, at most 200 bytes)
 *           removed count (2 bytes, big-endian), then for each removed device
 *               id (16 bytes, RFC 4122 order), name length (2 bytes, big-endian), name (UTF-8, at most 200 bytes),
 *               erase (1 byte: 1 = every staying device forgets what it received from this device, 0 = it is kept; any other value makes the file unreadable)
 *           wrap count (2 bytes, big-endian), then for each remaining device
 *               id (16 bytes), ephemeral public key (65 bytes), nonce (12 bytes), wrapped secret (41 bytes), see [RekeyWrap]
 * ```
 *
 * The old key opens it, so a removed device that still holds that key can read that it was removed; only a device with a
 * wrap for its own id and key can read the new link code (the maker is among the recipients, so it reads its own file the
 * same way). The file is named [fileNameFor] and goes where the group's sync
 * files go, so every method that carries them carries it.
 */
object RekeyCodec {
    private const val MAGIC_BYTES = 4
    private const val GROUP_ID_BYTES = 4
    private const val NONCE_BYTES = 12
    private const val TAG_BYTES = 16
    private const val HEADER_BYTES = MAGIC_BYTES + GROUP_ID_BYTES + NONCE_BYTES
    private const val ID_BYTES = 16
    private const val VERSION = 1
    private const val TRANSFORMATION = "AES/GCM/NoPadding"

    /** The longest name [seal] writes for a removed device, in UTF-8 bytes; a longer one is cut. */
    const val MAX_NAME_BYTES = 200

    private val magic = "ABR1".toByteArray(Charsets.US_ASCII)
    private val random = SecureRandom()

    /** Whether the bytes start like a rekey file ("ABR1"); it may still not open. */
    fun isRekey(bytes: ByteArray): Boolean =
        bytes.size >= HEADER_BYTES + TAG_BYTES && magic.indices.all { bytes[it] == magic[it] }

    /** The group id (8 lower-case hex characters) a rekey file is sealed for; null when it isn't one. */
    fun groupIdOf(bytes: ByteArray): String? =
        if (isRekey(bytes)) bytes.copyOfRange(MAGIC_BYTES, MAGIC_BYTES + GROUP_ID_BYTES).joinToString("") { "%02x".format(it) } else null

    /**
     * The name of the rekey file that [makerId] writes for the group [oldGroupId]: the first 6 bytes of SHA-256 over the UTF-8
     * of the group id, the maker's id ("D" format, lower case) and "rekey", as 12 lower-case hex characters, then ".absync". One
     * file per maker, so two devices that remove at the same time never overwrite each other. Shaped like a device's file name
     * ([SyncFolder.fileNameFor], whose hash input ends in a UUID, so the two never meet) so every method and the relay accept it.
     */
    fun fileNameFor(oldGroupId: String, makerId: UUID): String {
        val hash = MessageDigest.getInstance("SHA-256").digest((oldGroupId + makerId.toString() + "rekey").toByteArray(Charsets.UTF_8))
        return hash.take(6).joinToString("") { "%02x".format(it) } + SyncFolder.EXTENSION
    }

    /**
     * Which of two rekeys for the same group wins: the one whose maker has the lower id, comparing the ids' bytes (RFC 4122
     * order, as in the file) as unsigned numbers. Every device picks the same one, whatever order it saw them in.
     *
     * @return Negative when [first] wins, positive when [second] does.
     */
    fun compareMakers(first: UUID, second: UUID): Int {
        val a = RekeyWrap.idBytes(first)
        val b = RekeyWrap.idBytes(second)
        for (index in a.indices) {
            val difference = (a[index].toInt() and 0xFF) - (b[index].toInt() and 0xFF)
            if (difference != 0) {
                return difference
            }
        }

        return 0
    }

    fun seal(payload: RekeyPayload, oldKey: SyncKey): ByteArray {
        val plain = serialize(payload)
        val nonce = ByteArray(NONCE_BYTES)
        random.nextBytes(nonce)
        val header = magic + oldKey.groupIdBytes
        val cipher = Cipher.getInstance(TRANSFORMATION)
        cipher.init(Cipher.ENCRYPT_MODE, SecretKeySpec(oldKey.key, "AES"), GCMParameterSpec(TAG_BYTES * 8, nonce))
        cipher.updateAAD(header)
        // Java's GCM output is the ciphertext followed by the tag, as the layout has it.
        return header + nonce + cipher.doFinal(plain)
    }

    /** The payload; null when it isn't a rekey file of this group or key, was changed, or doesn't parse. */
    fun open(sealedBytes: ByteArray, oldKey: SyncKey): RekeyPayload? {
        if (!isRekey(sealedBytes) || !oldKey.groupIdBytes.indices.all { sealedBytes[MAGIC_BYTES + it] == oldKey.groupIdBytes[it] }) {
            return null
        }

        val plain = try {
            val cipher = Cipher.getInstance(TRANSFORMATION)
            cipher.init(
                Cipher.DECRYPT_MODE, SecretKeySpec(oldKey.key, "AES"),
                GCMParameterSpec(TAG_BYTES * 8, sealedBytes, MAGIC_BYTES + GROUP_ID_BYTES, NONCE_BYTES),
            )
            cipher.updateAAD(sealedBytes, 0, MAGIC_BYTES + GROUP_ID_BYTES)
            cipher.doFinal(sealedBytes, HEADER_BYTES, sealedBytes.size - HEADER_BYTES)
        } catch (e: GeneralSecurityException) {
            return null
        }

        return parse(plain)
    }

    /** The payload's bytes (the part inside the seal), exposed so two builds can be compared. */
    fun serialize(payload: RekeyPayload): ByteArray {
        val out = ByteArrayOutputStream()
        out.write(VERSION)
        out.write(ByteBuffer.allocate(8).putLong(payload.epoch).array())
        writeDevice(out, payload.removedBy)
        writeUInt16(out, payload.removed.size)
        for (removed in payload.removed) {
            writeDevice(out, removed)
            out.write(if (removed.erase) 1 else 0)
        }

        writeUInt16(out, payload.wraps.size)
        for (wrap in payload.wraps) {
            out.write(RekeyWrap.idBytes(wrap.deviceId))
            out.write(wrap.ephemeralPublicKey)
            out.write(wrap.nonce)
            out.write(wrap.wrapped)
        }

        return out.toByteArray()
    }

    /** The payload; null when the bytes aren't a complete version 1 payload. */
    fun parse(plain: ByteArray): RekeyPayload? {
        val reader = Reader(plain)
        if (reader.readByte() != VERSION) {
            return null
        }

        val epoch = reader.readLong() ?: return null
        val removedBy = readDevice(reader) ?: return null
        val removedCount = reader.readUInt16() ?: return null
        val removed = ArrayList<RekeyRemovedDevice>()
        repeat(removedCount) {
            val device = readDevice(reader) ?: return null
            val erase = reader.readByte()
            if (erase != 0 && erase != 1) {
                return null
            }

            removed += RekeyRemovedDevice(device.deviceId, device.name, erase == 1)
        }

        val wrapCount = reader.readUInt16() ?: return null
        val wraps = ArrayList<RekeyWrapRecord>()
        repeat(wrapCount) {
            val id = reader.readId() ?: return null
            val ephemeral = reader.readBytes(SyncDeviceKey.PUBLIC_KEY_BYTES) ?: return null
            val nonce = reader.readBytes(RekeyWrap.NONCE_BYTES) ?: return null
            val wrapped = reader.readBytes(RekeyWrap.WRAPPED_BYTES) ?: return null
            wraps += RekeyWrapRecord(id, ephemeral, nonce, wrapped)
        }

        return if (reader.atEnd) RekeyPayload(epoch, removedBy, removed, wraps) else null
    }

    // A device's id, then its name (length and UTF-8).
    private fun writeDevice(out: ByteArrayOutputStream, device: RekeyRemovedDevice) {
        out.write(RekeyWrap.idBytes(device.deviceId))
        val name = truncatedName(device.name)
        writeUInt16(out, name.size)
        out.write(name)
    }

    private fun readDevice(reader: Reader): RekeyRemovedDevice? {
        val id = reader.readId() ?: return null
        val nameLength = reader.readUInt16() ?: return null
        if (nameLength > MAX_NAME_BYTES) {
            return null
        }

        val name = reader.readBytes(nameLength) ?: return null
        return RekeyRemovedDevice(id, String(name, Charsets.UTF_8))
    }

    // At most MAX_NAME_BYTES of UTF-8, cut at a character boundary.
    private fun truncatedName(name: String): ByteArray {
        val bytes = name.toByteArray(Charsets.UTF_8)
        if (bytes.size <= MAX_NAME_BYTES) {
            return bytes
        }

        var length = MAX_NAME_BYTES
        while (length > 0 && (bytes[length].toInt() and 0xC0) == 0x80) {
            length--
        }

        return bytes.copyOf(length)
    }

    private fun writeUInt16(out: ByteArrayOutputStream, value: Int) {
        require(value in 0..0xFFFF) { "Too many entries for a rekey file." }
        out.write(value shr 8)
        out.write(value and 0xFF)
    }

    // Reads the payload front to back; every read is null when too few bytes are left.
    private class Reader(private val bytes: ByteArray) {
        private var position = 0

        val atEnd: Boolean get() = position == bytes.size

        /** The next byte, or -1 at the end. */
        fun readByte(): Int = if (position < bytes.size) bytes[position++].toInt() and 0xFF else -1

        fun readUInt16(): Int? {
            if (bytes.size - position < 2) return null
            val value = ((bytes[position].toInt() and 0xFF) shl 8) or (bytes[position + 1].toInt() and 0xFF)
            position += 2
            return value
        }

        fun readLong(): Long? {
            if (bytes.size - position < 8) return null
            val value = ByteBuffer.wrap(bytes, position, 8).long
            position += 8
            return value
        }

        fun readId(): UUID? {
            if (bytes.size - position < ID_BYTES) return null
            val id = RekeyWrap.idOf(bytes, position)
            position += ID_BYTES
            return id
        }

        fun readBytes(count: Int): ByteArray? {
            if (bytes.size - position < count) return null
            val value = bytes.copyOfRange(position, position + count)
            position += count
            return value
        }
    }
}
