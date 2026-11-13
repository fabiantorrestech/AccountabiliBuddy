package com.accountabilibuddy.shared.goals

import java.nio.ByteBuffer
import java.security.MessageDigest
import java.util.UUID

/** A name-based (v5, SHA-1) id as the PC makes them: the namespace's RFC 4122 bytes, then the name's UTF-8 bytes. */
internal object GoalNameId {
    fun of(namespace: UUID, name: String): UUID {
        val space = ByteBuffer.allocate(16).putLong(namespace.mostSignificantBits).putLong(namespace.leastSignificantBits).array()
        val hash = MessageDigest.getInstance("SHA-1").digest(space + name.toByteArray(Charsets.UTF_8))
        val id = hash.copyOf(16)
        id[6] = ((id[6].toInt() and 0x0F) or 0x50).toByte()
        id[8] = ((id[8].toInt() and 0x3F) or 0x80).toByte()
        val bytes = ByteBuffer.wrap(id)
        return UUID(bytes.long, bytes.long)
    }
}
