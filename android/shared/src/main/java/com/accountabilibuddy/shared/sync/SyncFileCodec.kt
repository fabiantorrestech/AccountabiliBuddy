package com.accountabilibuddy.shared.sync

import kotlinx.serialization.SerializationException
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonObject
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

    fun seal(file: SyncFile, key: SyncKey): ByteArray {
        val compressed = deflate(SyncFileJson.write(file).toString().toByteArray(Charsets.UTF_8))
        val nonce = ByteArray(NONCE_BYTES)
        random.nextBytes(nonce)

        val cipher = Cipher.getInstance(TRANSFORMATION)
        cipher.init(Cipher.ENCRYPT_MODE, SecretKeySpec(key.key, "AES"), GCMParameterSpec(TAG_BYTES * 8, nonce))
        // Java's GCM output is the ciphertext followed by the tag, as the layout has it.
        return magic + key.groupIdBytes + nonce + cipher.doFinal(compressed)
    }

    /**
     * The file, or null when it isn't one this group's devices wrote or can't be read: another group or key, a changed
     * or cut-off file, a header other than "ABS1", JSON that isn't UTF-8 or isn't a complete sync file, or a format
     * version newer than this app's.
     */
    fun open(sealedBytes: ByteArray, key: SyncKey): SyncFile? {
        if (sealedBytes.size < HEADER_BYTES + TAG_BYTES
            || !sealedBytes.startsWith(magic, 0)
            || !sealedBytes.startsWith(key.groupIdBytes, MAGIC_BYTES)
        ) {
            return null
        }

        val compressed = try {
            val cipher = Cipher.getInstance(TRANSFORMATION)
            val nonce = GCMParameterSpec(TAG_BYTES * 8, sealedBytes, MAGIC_BYTES + GROUP_ID_BYTES, NONCE_BYTES)
            cipher.init(Cipher.DECRYPT_MODE, SecretKeySpec(key.key, "AES"), nonce)
            cipher.doFinal(sealedBytes, HEADER_BYTES, sealedBytes.size - HEADER_BYTES)
        } catch (e: GeneralSecurityException) {
            // The tag doesn't match: another key, or the bytes were changed.
            return null
        }

        val text = inflate(compressed)?.let(::decodeUtf8) ?: return null
        val json = try {
            Json.parseToJsonElement(text)
        } catch (e: SerializationException) {
            return null
        }

        return (json as? JsonObject)?.let(SyncFileJson::read)
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

    /** The inflated bytes, or null when they aren't complete raw Deflate data or exceed [MAX_JSON_BYTES]. */
    private fun inflate(compressed: ByteArray): ByteArray? {
        val inflater = Inflater(true)
        try {
            inflater.setInput(compressed)
            val output = ByteArrayOutputStream()
            val buffer = ByteArray(BUFFER_BYTES)
            while (!inflater.finished()) {
                val read = inflater.inflate(buffer)
                if (read == 0 && !inflater.finished()) {
                    // It wants more input (cut off) or a dictionary: not a file a device wrote.
                    return null
                }

                if (output.size() + read > MAX_JSON_BYTES) {
                    return null
                }

                output.write(buffer, 0, read)
            }

            return output.toByteArray()
        } catch (e: DataFormatException) {
            return null
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
