package com.accountabilibuddy.companion.data

import com.accountabilibuddy.companion.data.JsonValues.int
import com.accountabilibuddy.companion.data.JsonValues.string
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.jsonObject
import java.util.Base64
import javax.crypto.AEADBadTagException
import javax.crypto.Cipher
import javax.crypto.SecretKeyFactory
import javax.crypto.spec.GCMParameterSpec
import javax.crypto.spec.PBEKeySpec
import javax.crypto.spec.SecretKeySpec

/**
 * The outer layer of a Windows backup file (*.abbackup), like the Core's BackupEnvelope: readable content, or
 * AES-GCM ciphertext with the key from the password (PBKDF2-SHA256). Backups made "for this computer only" (DPAPI)
 * can't be opened anywhere else.
 */
object BackupFile {
    private const val FORMAT = "accountabilibuddy-backup"
    private const val VERSION = 1
    private const val KEY_BITS = 256
    private const val TAG_BITS = 128
    private val associatedData = "$FORMAT/$VERSION".toByteArray(Charsets.UTF_8)

    /** True for a backup file (any protection); false for other JSON. */
    fun isBackup(text: String): Boolean = runCatching { envelope(text) }.isSuccess

    fun needsPassword(text: String): Boolean = envelope(text).string("protection") == "Password"

    /** The content object. @throws ImportException with a message for the user. */
    fun open(text: String, password: String? = null): JsonObject {
        val envelope = envelope(text)
        return when (envelope.string("protection")) {
            "None" -> envelope["content"] as? JsonObject ?: throw ImportException("The backup has no content.")
            "Password" -> {
                if (password.isNullOrEmpty()) {
                    throw ImportException("This backup is password-protected. Enter its password.")
                }

                Json.parseToJsonElement(decrypt(envelope, password)).jsonObject
            }
            "ThisComputer" -> throw ImportException(
                "This backup was made for one Windows computer only and can't be opened on the phone. On the PC, export a backup without secrets or with a password.",
            )
            else -> throw ImportException("The backup's protection isn't recognised.")
        }
    }

    private fun envelope(text: String): JsonObject {
        val envelope = try {
            Json.parseToJsonElement(text) as? JsonObject
        } catch (e: Exception) {
            null
        }

        if (envelope == null || envelope.string("format") != FORMAT) {
            throw ImportException("This isn't an AccountabiliBuddy backup file.")
        }

        if ((envelope.int("version") ?: 0) > VERSION) {
            throw ImportException("This backup was made by a newer AccountabiliBuddy. Update the phone app first.")
        }

        return envelope
    }

    private fun decrypt(envelope: JsonObject, password: String): String {
        val iterations = envelope.int("iterations") ?: 600_000
        val salt = bytes(envelope, "salt")
        val nonce = bytes(envelope, "nonce")
        val tag = bytes(envelope, "tag")
        val data = bytes(envelope, "data")

        val factory = SecretKeyFactory.getInstance("PBKDF2WithHmacSHA256")
        val key = factory.generateSecret(PBEKeySpec(password.toCharArray(), salt, iterations, KEY_BITS)).encoded
        val cipher = Cipher.getInstance("AES/GCM/NoPadding")
        cipher.init(Cipher.DECRYPT_MODE, SecretKeySpec(key, "AES"), GCMParameterSpec(TAG_BITS, nonce))
        cipher.updateAAD(associatedData)
        return try {
            String(cipher.doFinal(data + tag), Charsets.UTF_8)
        } catch (e: AEADBadTagException) {
            throw ImportException("Wrong password (or the file was changed).", e)
        }
    }

    private fun bytes(envelope: JsonObject, name: String): ByteArray {
        val text = envelope.string(name) ?: throw ImportException("The backup is missing \"$name\".")
        return try {
            Base64.getDecoder().decode(text)
        } catch (e: IllegalArgumentException) {
            throw ImportException("The backup's \"$name\" is damaged.", e)
        }
    }
}
