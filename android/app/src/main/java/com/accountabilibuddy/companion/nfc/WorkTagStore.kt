package com.accountabilibuddy.companion.nfc

import android.content.Context
import com.accountabilibuddy.shared.TokenVault
import java.security.MessageDigest
import java.security.SecureRandom
import java.util.Base64

/**
 * The finished-work tag's secret (ADR 67): 32 random bytes written to the user's NFC tag at set-up, kept here sealed by
 * [TokenVault] under its own Keystore key in the private preferences "workTag". Device-local: never synced, logged or in a backup
 * (the backup's file list doesn't name it). A tag counts only when it carries this secret.
 */
class WorkTagStore(context: Context) {
    private val prefs = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)

    /** A tag is registered on this phone. */
    val isSet: Boolean get() = prefs.contains(KEY_SECRET)

    /** A new random secret, not saved until [save] (once it was written to the tag and read back). */
    fun newSecret(): String {
        val bytes = ByteArray(SECRET_BYTES).also { SecureRandom().nextBytes(it) }
        return Base64.getUrlEncoder().withoutPadding().encodeToString(bytes)
    }

    fun save(secret: String) {
        prefs.edit().putString(KEY_SECRET, TokenVault.seal(secret, TokenVault.WORK_TAG_ALIAS)).apply()
    }

    fun forget() {
        prefs.edit().remove(KEY_SECRET).apply()
    }

    /** [read] is this phone's secret (compared in constant time); false when no tag is registered or the secret can't be opened. */
    fun matches(read: String?): Boolean {
        val secret = prefs.getString(KEY_SECRET, null)?.let { TokenVault.open(it, TokenVault.WORK_TAG_ALIAS) } ?: return false
        return read != null && MessageDigest.isEqual(secret.toByteArray(), read.toByteArray())
    }

    companion object {
        const val PREFS = "workTag"
        private const val KEY_SECRET = "secret"
        private const val SECRET_BYTES = 32
    }
}
