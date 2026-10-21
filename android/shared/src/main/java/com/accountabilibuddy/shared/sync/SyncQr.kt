package com.accountabilibuddy.shared.sync

import java.security.MessageDigest
import java.util.Base64

/**
 * A sealed sync file as QR codes the phone's own camera opens (ADR 22), as on the PC (Core's SyncQr): links
 * "accountabilibuddy://sync?g=<group id>&f=<check>&p=<i>-<n>&d=<part>", one per code. The check is the first 4 bytes
 * of the whole file's SHA-256 in hex: it tells the codes of one file from those of another (the PC draws new codes
 * each time) and proves the parts were put back together right. The part is base64url without padding.
 * [SyncQrCollector] reads them back, in any order.
 */
object SyncQr {
    /** The start of every link, as written (lower case); a reader takes its letters in any case. */
    const val LINK_START = "accountabilibuddy://sync?"

    /** Sealed bytes per code: a link of about 1,650 characters, which a phone reads off a screen. */
    const val BYTES_PER_CODE = 1200

    /** The most codes the PC shows for one file (its SyncFileBuilder.ForQr fits a day into them). */
    const val MAX_CODES = 4

    /** The most parts a file can be cut into (two digits in a link). */
    const val MAX_PARTS = 99

    private const val CHECK_BYTES = 4
    private const val HEX_DIGITS = "0123456789abcdef"

    /**
     * The links in order, from 1 to n.
     *
     * @throws IllegalArgumentException The bytes are empty, the group id isn't 8 lower-case hex characters, the code
     * size is under 1, or they'd need more than [MAX_PARTS] codes.
     */
    fun links(sealedBytes: ByteArray, groupId: String, maxBytesPerCode: Int = BYTES_PER_CODE): List<String> {
        require(maxBytesPerCode >= 1) { "A code holds at least one byte." }
        require(sealedBytes.isNotEmpty()) { "There are no bytes to show." }
        require(isHexId(groupId)) { "The group id is 8 lower-case hex characters." }
        val total = (sealedBytes.size + maxBytesPerCode - 1) / maxBytesPerCode
        require(total <= MAX_PARTS) { "The file needs more than $MAX_PARTS codes." }

        val check = checkOf(sealedBytes)
        return (0 until total).map { index ->
            val start = index * maxBytesPerCode
            val part = sealedBytes.copyOfRange(start, minOf(start + maxBytesPerCode, sealedBytes.size))
            "${LINK_START}g=$groupId&f=$check&p=${index + 1}-$total&d=${toBase64Url(part)}"
        }
    }

    /** The file's check: the first 4 bytes of its SHA-256, in lower-case hex. */
    internal fun checkOf(sealedBytes: ByteArray): String {
        val hash = MessageDigest.getInstance("SHA-256").digest(sealedBytes)
        return buildString(CHECK_BYTES * 2) {
            for (index in 0 until CHECK_BYTES) {
                append(HEX_DIGITS[(hash[index].toInt() shr 4) and 0x0F])
                append(HEX_DIGITS[hash[index].toInt() and 0x0F])
            }
        }
    }

    /** A group id or a check: 8 characters of 0-9 and a-f. */
    internal fun isHexId(text: String): Boolean = text.length == CHECK_BYTES * 2 && text.all { it in HEX_DIGITS }

    /** Base64url without padding. */
    internal fun toBase64Url(bytes: ByteArray): String = Base64.getUrlEncoder().withoutPadding().encodeToString(bytes)

    /** The bytes, or null when the text isn't base64url without padding. */
    internal fun fromBase64Url(text: String): ByteArray? {
        if (text.isEmpty() || text.length % 4 == 1
            || !text.all { it in 'A'..'Z' || it in 'a'..'z' || it in '0'..'9' || it == '-' || it == '_' }
        ) {
            return null
        }

        return try {
            Base64.getUrlDecoder().decode(text)
        } catch (e: IllegalArgumentException) {
            null
        }
    }
}

/**
 * Puts a sealed sync file back together from its QR links ([SyncQr.links]), scanned in any order, as on the PC (Core's
 * SyncQrCollector). A link of another group, another count or another file (a new set of codes) starts over with that
 * one. Once every part is in, the parts are checked against the file's check; a set that doesn't match is dropped.
 * Opening the file ([SyncFileCodec.open]) is the caller's step.
 */
class SyncQrCollector {
    private var parts: Array<ByteArray?> = emptyArray()
    private var check: String? = null

    /** The group id of the codes being collected; null before the first. */
    var groupId: String? = null
        private set

    /** How many different codes of the current file are in. */
    var received: Int = 0
        private set

    /** How many codes the current file has; 0 before the first. */
    val total: Int
        get() = parts.size

    /** The sealed file, once every code is in and the check matches; else null. */
    var complete: ByteArray? = null
        private set

    /**
     * True when the link is a sync code and was taken in (a code scanned again is taken but not counted twice); false
     * when it isn't one, changing nothing, or when it completed a set that fails the check, which is dropped.
     */
    fun add(link: String): Boolean {
        val part = read(link) ?: return false
        if (part.groupId != groupId || part.check != check || part.total != total) {
            reset()
            groupId = part.groupId
            check = part.check
            parts = arrayOfNulls(part.total)
        }

        if (parts[part.index - 1] == null) {
            parts[part.index - 1] = part.data
            received++
        }

        if (received == total && complete == null) {
            val whole = parts.fold(ByteArray(0)) { all, data -> all + data!! }
            if (SyncQr.checkOf(whole) != check) {
                reset()
                return false
            }

            complete = whole
        }

        return true
    }

    /** Forgets every code, e.g. after the complete file didn't open. */
    fun reset() {
        parts = emptyArray()
        check = null
        groupId = null
        received = 0
        complete = null
    }

    private class Part(val groupId: String, val check: String, val index: Int, val total: Int, val data: ByteArray)

    /**
     * Reads a link: its start in any case (ASCII), then "&"-separated names and values, each name once; g and f are 8
     * lower-case hex characters, p is "i-n" (1 ≤ i ≤ n ≤ 99, digits only), d is base64url. Other names are ignored.
     */
    private fun read(link: String): Part? {
        // ASCII letters only (as the PC's Ascii.EqualsIgnoreCase), so the phone reads exactly the links the PC does.
        if (link.length < SyncQr.LINK_START.length || SyncQr.LINK_START.indices.any { asciiLower(link[it]) != SyncQr.LINK_START[it] }) {
            return null
        }

        val values = mutableMapOf<String, String>()
        for (pair in link.substring(SyncQr.LINK_START.length).split('&')) {
            val equals = pair.indexOf('=')
            if (equals <= 0 || values.put(pair.substring(0, equals), pair.substring(equals + 1)) != null) {
                return null
            }
        }

        val groupId = values["g"]?.takeIf(SyncQr::isHexId) ?: return null
        val check = values["f"]?.takeIf(SyncQr::isHexId) ?: return null
        val numbers = values["p"]?.split('-') ?: return null
        if (numbers.size != 2) {
            return null
        }

        val index = readCount(numbers[0]) ?: return null
        val total = readCount(numbers[1]) ?: return null
        if (index < 1 || index > total || total > SyncQr.MAX_PARTS) {
            return null
        }

        val data = values["d"]?.let(SyncQr::fromBase64Url) ?: return null
        return Part(groupId, check, index, total, data)
    }

    private fun asciiLower(character: Char): Char = if (character in 'A'..'Z') character + ('a' - 'A') else character

    /** One or two digits, nothing else. */
    private fun readCount(text: String): Int? =
        if (text.length in 1..2 && text.all { it in '0'..'9' }) text.toInt() else null
}
