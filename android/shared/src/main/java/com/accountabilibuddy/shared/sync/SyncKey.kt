package com.accountabilibuddy.shared.sync

import java.security.SecureRandom
import javax.crypto.Mac
import javax.crypto.spec.SecretKeySpec

/**
 * The sync group's key (ADR 22), derived from the link code's 200 random bits as on the PC (Core's SyncKey):
 * HKDF-SHA256 with salt "AccountabiliBuddy sync" gives the 32-byte AES-GCM [key] (info "key") and a 4-byte group id
 * (info "group") that tells one group's files from another's without trying the key. Made from a code with
 * [SyncKeyCode.parse].
 *
 * @property key The AES-256-GCM key (32 bytes).
 * @property groupId The group id as 8 lower-case hex characters; its 4 bytes start every sealed file after "ABS1".
 */
class SyncKey(val key: ByteArray, val groupId: String) {
    init {
        require(key.size == KEY_BYTES) { "The sync key is $KEY_BYTES bytes." }
        require(groupId.length == GROUP_ID_BYTES * 2 && groupId.all { it in HEX_DIGITS }) {
            "The group id is ${GROUP_ID_BYTES * 2} lower-case hex characters."
        }
    }

    /** The group id's 4 bytes, as they follow "ABS1" in a sealed file. */
    internal val groupIdBytes: ByteArray
        get() = ByteArray(GROUP_ID_BYTES) { groupId.substring(it * 2, it * 2 + 2).toInt(16).toByte() }

    companion object {
        /** The link code's 200 bits. */
        const val SECRET_BYTES = 25

        private const val KEY_BYTES = 32
        private const val GROUP_ID_BYTES = 4
        private const val HEX_DIGITS = "0123456789abcdef"
        private const val HMAC = "HmacSHA256"

        private val salt = "AccountabiliBuddy sync".toByteArray(Charsets.UTF_8)
        private val keyInfo = "key".toByteArray(Charsets.UTF_8)
        private val groupInfo = "group".toByteArray(Charsets.UTF_8)

        /** @throws IllegalArgumentException The secret isn't [SECRET_BYTES] bytes. */
        fun fromSecret(secret: ByteArray): SyncKey {
            require(secret.size == SECRET_BYTES) { "The link code's secret is $SECRET_BYTES bytes." }
            val key = hkdf(secret, keyInfo, KEY_BYTES)
            val groupId = hkdf(secret, groupInfo, GROUP_ID_BYTES)
            return SyncKey(key, toHex(groupId))
        }

        /** HKDF-SHA256 (RFC 5869) with [salt]: extract, then expand to [length] bytes. */
        private fun hkdf(secret: ByteArray, info: ByteArray, length: Int): ByteArray {
            val mac = Mac.getInstance(HMAC)
            mac.init(SecretKeySpec(salt, HMAC))
            val pseudoRandomKey = mac.doFinal(secret)
            mac.init(SecretKeySpec(pseudoRandomKey, HMAC))

            val output = ByteArray(length)
            var block = ByteArray(0)
            var written = 0
            var counter = 1
            while (written < length) {
                mac.update(block)
                mac.update(info)
                mac.update(counter.toByte())
                block = mac.doFinal()
                val count = minOf(block.size, length - written)
                block.copyInto(output, written, 0, count)
                written += count
                counter++
            }

            pseudoRandomKey.fill(0)
            block.fill(0)
            return output
        }

        private fun toHex(bytes: ByteArray): String = buildString(bytes.size * 2) {
            for (value in bytes) {
                append(HEX_DIGITS[(value.toInt() shr 4) and 0x0F])
                append(HEX_DIGITS[value.toInt() and 0x0F])
            }
        }
    }
}

/**
 * The link code (ADR 22), as on the PC (Core's SyncKeyCode): the sync key's 200 random bits as 40 Crockford base32
 * characters (0-9 and A-Z without I, L, O and U), most significant bit first, shown in groups of 5. Whoever has it can
 * read and write the group's files, so the phone keeps it only through TokenVault.
 */
object SyncKeyCode {
    private const val ALPHABET = "0123456789ABCDEFGHJKMNPQRSTVWXYZ"
    private const val CODE_LENGTH = 40
    private const val BITS_PER_CHARACTER = 5
    private const val CHARACTER_MASK = (1 shl BITS_PER_CHARACTER) - 1
    private const val GROUP_LENGTH = 5

    private val random = SecureRandom()

    /** A new code: 40 characters from 25 random bytes. */
    fun newCode(): String {
        val secret = ByteArray(SyncKey.SECRET_BYTES)
        random.nextBytes(secret)
        try {
            return encode(secret)
        } finally {
            secret.fill(0)
        }
    }

    /**
     * Reads a typed or scanned code. Spaces, dashes and lower case are ignored; what's left must be exactly 40
     * characters of the alphabet (I, L, O and U are refused, not read as 1 or 0). Null when it isn't a code.
     */
    fun parse(text: String): SyncKey? {
        val code = normalize(text)
        if (code.length != CODE_LENGTH) {
            return null
        }

        val secret = ByteArray(SyncKey.SECRET_BYTES)
        try {
            var buffer = 0
            var bufferedBits = 0
            var written = 0
            for (character in code) {
                val value = ALPHABET.indexOf(character)
                if (value < 0) {
                    return null
                }

                buffer = (buffer shl BITS_PER_CHARACTER) or value
                bufferedBits += BITS_PER_CHARACTER
                if (bufferedBits >= 8) {
                    bufferedBits -= 8
                    secret[written++] = (buffer shr bufferedBits).toByte()
                    buffer = buffer and ((1 shl bufferedBits) - 1)
                }
            }

            return SyncKey.fromSecret(secret)
        } finally {
            secret.fill(0)
        }
    }

    /** The code in groups of 5 joined by dashes, e.g. "8KQ2V-7XHPM-…". */
    fun display(code: String): String = normalize(code).chunked(GROUP_LENGTH).joinToString("-")

    private fun normalize(text: String): String = buildString(text.length) {
        for (character in text) {
            if (character != '-' && !isSpace(character)) {
                append(character.uppercaseChar())
            }
        }
    }

    // The characters .NET's char.IsWhiteSpace skips, so the same pasted text reads the same on the phone and the PC.
    private fun isSpace(character: Char): Boolean {
        if (character in '\t'..'\r' || character == '\u0085') {
            return true
        }

        val type = Character.getType(character)
        return type == Character.SPACE_SEPARATOR.toInt()
            || type == Character.LINE_SEPARATOR.toInt()
            || type == Character.PARAGRAPH_SEPARATOR.toInt()
    }

    private fun encode(secret: ByteArray): String = buildString(CODE_LENGTH) {
        var buffer = 0
        var bufferedBits = 0
        for (value in secret) {
            buffer = (buffer shl 8) or (value.toInt() and 0xFF)
            bufferedBits += 8
            while (bufferedBits >= BITS_PER_CHARACTER) {
                bufferedBits -= BITS_PER_CHARACTER
                append(ALPHABET[(buffer shr bufferedBits) and CHARACTER_MASK])
            }

            buffer = buffer and ((1 shl bufferedBits) - 1)
        }
    }
}
