package com.accountabilibuddy.shared.goals

import java.nio.ByteBuffer
import java.security.MessageDigest
import java.util.UUID

/**
 * A task label's id for its name, the same as the PC's `TaskLabel.IdFor`: SHA-1 over a fixed namespace's bytes (RFC 4122 byte
 * order) and the UTF-8 of the trimmed, lower-cased name, as a version 5 id. Every device reaches the same label from a name
 * alone (an older device that sends only the category name), so "Work" and "work" are one label.
 */
object GoalLabelId {
    // The PC's TaskLabel.NameSpace (the same bytes on every device).
    private val NAMESPACE: UUID = UUID.fromString("6f1b9a4e-3c2d-4e8b-9a71-5d0c2b7e4f13")

    fun idFor(name: String): UUID {
        val space = ByteBuffer.allocate(16).putLong(NAMESPACE.mostSignificantBits).putLong(NAMESPACE.leastSignificantBits).array()
        val text = lowerInvariant(name.trim()).toByteArray(Charsets.UTF_8)
        val hash = MessageDigest.getInstance("SHA-1").digest(space + text)
        val id = hash.copyOf(16)
        id[6] = ((id[6].toInt() and 0x0F) or 0x50).toByte()
        id[8] = ((id[8].toInt() and 0x3F) or 0x80).toByte()
        val bytes = ByteBuffer.wrap(id)
        return UUID(bytes.long, bytes.long)
    }

    // .NET's ToLowerInvariant maps each character on its own (no locale, no context such as a final sigma); so does this.
    private fun lowerInvariant(text: String): String {
        val lowered = StringBuilder(text.length)
        var index = 0
        while (index < text.length) {
            val codePoint = text.codePointAt(index)
            lowered.appendCodePoint(Character.toLowerCase(codePoint))
            index += Character.charCount(codePoint)
        }

        return lowered.toString()
    }
}
