package com.accountabilibuddy.shared.sync

import kotlinx.serialization.SerializationException
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.intOrNull
import java.io.ByteArrayOutputStream
import java.nio.ByteBuffer
import java.nio.charset.CharacterCodingException
import java.nio.charset.CodingErrorAction
import java.security.GeneralSecurityException
import java.security.SecureRandom
import java.util.zip.DataFormatException
import java.util.zip.Deflater
import java.util.zip.Inflater
import javax.crypto.Cipher
import javax.crypto.spec.GCMParameterSpec
import javax.crypto.spec.SecretKeySpec

/**
 * Seals and opens sync files (ADR 22) in the PC's layout (Core's SyncFileCodec), so either side opens the other's:
 * "ABS1", the key's 4-byte group id, a 12-byte random nonce, and then AES-256-GCM over the raw-Deflated UTF-8 JSON of
 * the [SyncFile] ([SyncFileJson]), ending with the 16-byte tag.
 */
object SyncFileCodec {
    private const val MAGIC_BYTES = 4
    private const val GROUP_ID_BYTES = 4
    private const val NONCE_BYTES = 12
    private const val TAG_BYTES = 16
    private const val HEADER_BYTES = MAGIC_BYTES + GROUP_ID_BYTES + NONCE_BYTES
    private const val TRANSFORMATION = "AES/GCM/NoPadding"
    private const val BUFFER_BYTES = 64 * 1024

    // Only a device holding the key can make a file that opens; this keeps a broken one from filling memory.
    private const val MAX_JSON_BYTES = 16 * 1024 * 1024

    private val magic = "ABS1".toByteArray(Charsets.US_ASCII)
    private val random = SecureRandom()

    fun seal(file: SyncFile, key: SyncKey): ByteArray = sealJson(sealedText(file).toByteArray(Charsets.UTF_8), key, magic)

    /**
     * The file as JSON text, written by [com.accountabilibuddy.shared.goals.GoalJson.text] (never JsonObject.toString()): a DateTimeOffset value is
     * marked inside the JSON tree (U+0001, GoalJson.timeValue) and only that writer drops the mark, as .NET writes the time straight. With the
     * mark in the text the PC cannot read the time, and leaves out every goal, area, task and label of this phone.
     */
    fun sealedText(file: SyncFile): String = com.accountabilibuddy.shared.goals.GoalJson.text(SyncFileJson.write(file))

    /**
     * Seals [json] the way [seal] does under another 4-byte [header] (the goal history files, ADR 79: "ABM1" for a month, "ABY1" for a
     * year; the PC's `SyncFileCodec.SealJson`): header, the key's group id, a random nonce, AES-256-GCM over the raw-Deflated JSON, the tag.
     */
    fun sealJson(json: ByteArray, key: SyncKey, header: ByteArray): ByteArray {
        val compressed = deflate(json)
        val nonce = ByteArray(NONCE_BYTES)
        random.nextBytes(nonce)

        val cipher = Cipher.getInstance(TRANSFORMATION)
        cipher.init(Cipher.ENCRYPT_MODE, SecretKeySpec(key.key, "AES"), GCMParameterSpec(TAG_BYTES * 8, nonce))
        // Java's GCM output is the ciphertext followed by the tag, as the layout has it.
        return header + key.groupIdBytes + nonce + cipher.doFinal(compressed)
    }

    /**
     * The inflated JSON of bytes sealed by [sealJson] under [header] with this group's key; null for another header, group or key, changed
     * or cut-off bytes, or JSON over the 16 MB limit (the PC's `SyncFileCodec.OpenJson`).
     */
    fun openJson(sealedBytes: ByteArray, key: SyncKey, header: ByteArray): ByteArray? = openJsonDetailed(sealedBytes, key, header).first

    /** [openJson], saying why it gave null (OTHER_GROUP only for a header of [header] with another group id; ADR 86). */
    private fun openJsonDetailed(sealedBytes: ByteArray, key: SyncKey, header: ByteArray): Pair<ByteArray?, SyncFileOpenFailure?> {
        if (sealedBytes.size < HEADER_BYTES + TAG_BYTES || !sealedBytes.startsWith(header, 0)) {
            return null to SyncFileOpenFailure.DAMAGED
        }

        if (!sealedBytes.startsWith(key.groupIdBytes, MAGIC_BYTES)) {
            return null to SyncFileOpenFailure.OTHER_GROUP
        }

        val compressed = try {
            val cipher = Cipher.getInstance(TRANSFORMATION)
            val nonce = GCMParameterSpec(TAG_BYTES * 8, sealedBytes, MAGIC_BYTES + GROUP_ID_BYTES, NONCE_BYTES)
            cipher.init(Cipher.DECRYPT_MODE, SecretKeySpec(key.key, "AES"), nonce)
            cipher.doFinal(sealedBytes, HEADER_BYTES, sealedBytes.size - HEADER_BYTES)
        } catch (e: GeneralSecurityException) {
            // The tag doesn't match: another key, or the bytes were changed.
            return null to SyncFileOpenFailure.DAMAGED
        }

        val inflated = inflate(compressed)
        return inflated.bytes to if (inflated.bytes != null) null else if (inflated.tooBig) SyncFileOpenFailure.TOO_BIG else SyncFileOpenFailure.DAMAGED
    }

    /** A sync file ("ABS1") whose header names another group than [key]'s: a leftover (from before a device removal), not a problem. */
    fun isOtherGroups(sealedBytes: ByteArray, key: SyncKey): Boolean =
        sealedBytes.size >= MAGIC_BYTES + GROUP_ID_BYTES
            && sealedBytes.startsWith(magic, 0)
            && !sealedBytes.startsWith(key.groupIdBytes, MAGIC_BYTES)

    /**
     * The file, or null when it isn't one this group's devices wrote or can't be read: another group or key, a changed
     * or cut-off file, a header other than "ABS1", JSON that isn't UTF-8 or isn't a complete sync file, or a format
     * version newer than this app's ([tryOpen] says which).
     */
    fun open(sealedBytes: ByteArray, key: SyncKey): SyncFile? = tryOpen(sealedBytes, key).file

    /**
     * [open] with the reason it did not open (ADR 86; the PC's `TryOpen`): DAMAGED, OTHER_GROUP (an "ABS1" file of another group), TOO_BIG
     * (JSON over 16 MB) or NEWER_FORMAT (the JSON's own format version is above this app's, told before the shape is read).
     */
    fun tryOpen(sealedBytes: ByteArray, key: SyncKey): SyncFileOpenResult {
        val (bytes, failure) = openJsonDetailed(sealedBytes, key, magic)
        val text = bytes?.let(::decodeUtf8) ?: return SyncFileOpenResult(null, failure ?: SyncFileOpenFailure.DAMAGED)
        val json = try {
            Json.parseToJsonElement(text)
        } catch (e: SerializationException) {
            return SyncFileOpenResult(null, SyncFileOpenFailure.DAMAGED)
        }

        if (json is JsonObject && isNewerFormat(json)) return SyncFileOpenResult(null, SyncFileOpenFailure.NEWER_FORMAT)
        val file = (json as? JsonObject)?.let(SyncFileJson::read) ?: return SyncFileOpenResult(null, SyncFileOpenFailure.DAMAGED)
        // ADR 79: every goal item the merge cannot use is left out as the file opens (the PC's WithCleanGoalParts); ADR 86: the receipts are
        // cleaned to the known codes and their bounds (WithCleanReceipts).
        return SyncFileOpenResult(file.copy(goalParts = SyncGoalParts.cleanOpen(file.goalParts), receipts = SyncReceiptParts.clean(file.receipts)), null)
    }

    private fun isNewerFormat(json: JsonObject): Boolean {
        val version = json["formatVersion"] as? JsonPrimitive ?: return false
        return !version.isString && (version.intOrNull ?: return false) > SyncFile.CURRENT_FORMAT_VERSION
    }

    private fun ByteArray.startsWith(prefix: ByteArray, offset: Int): Boolean =
        prefix.indices.all { this[offset + it] == prefix[it] }

    /** Raw Deflate (no zlib header), as .NET's DeflateStream writes and reads it. */
    private fun deflate(json: ByteArray): ByteArray {
        val deflater = Deflater(Deflater.DEFAULT_COMPRESSION, true)
        try {
            deflater.setInput(json)
            deflater.finish()
            val output = ByteArrayOutputStream()
            val buffer = ByteArray(BUFFER_BYTES)
            while (!deflater.finished()) {
                output.write(buffer, 0, deflater.deflate(buffer))
            }

            return output.toByteArray()
        } finally {
            deflater.end()
        }
    }

    private class Inflated(val bytes: ByteArray?, val tooBig: Boolean)

    /** The inflated bytes, or none when they aren't complete raw Deflate data or exceed [MAX_JSON_BYTES] ([Inflated.tooBig]). */
    private fun inflate(compressed: ByteArray): Inflated {
        val inflater = Inflater(true)
        try {
            inflater.setInput(compressed)
            val output = ByteArrayOutputStream()
            val buffer = ByteArray(BUFFER_BYTES)
            while (!inflater.finished()) {
                val read = inflater.inflate(buffer)
                if (read == 0 && !inflater.finished()) {
                    // It wants more input (cut off) or a dictionary: not a file a device wrote.
                    return Inflated(null, false)
                }

                if (output.size() + read > MAX_JSON_BYTES) {
                    return Inflated(null, true)
                }

                output.write(buffer, 0, read)
            }

            return Inflated(output.toByteArray(), false)
        } catch (e: DataFormatException) {
            return Inflated(null, false)
        } finally {
            inflater.end()
        }
    }

    /** Null when the bytes aren't UTF-8, as the PC refuses them (rather than reading replacement characters). */
    private fun decodeUtf8(bytes: ByteArray): String? = try {
        Charsets.UTF_8.newDecoder()
            .onMalformedInput(CodingErrorAction.REPORT)
            .onUnmappableCharacter(CodingErrorAction.REPORT)
            .decode(ByteBuffer.wrap(bytes))
            .toString()
    } catch (e: CharacterCodingException) {
        null
    }
}
