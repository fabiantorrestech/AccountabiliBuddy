package com.accountabilibuddy.companion.engine

import java.util.UUID

/**
 * The same colour per profile as the Windows app's schedule grid: its palette, picked by .NET's Guid hash
 * (the four 32-bit words of the GUID's memory layout XORed together), so a profile looks the same on both.
 */
object ProfileColors {
    private val palette = longArrayOf(
        0xFF0F766E, // teal
        0xFF2563EB, // blue
        0xFF9333EA, // purple
        0xFFDB2777, // pink
        0xFFC2410C, // orange
        0xFF4D7C0F, // green
        0xFFA16207, // amber
        0xFF475569, // slate
    )

    /** ARGB colour for a profile. */
    fun argbFor(profileId: UUID): Long {
        val index = (dotNetHashCode(profileId).toUInt() % palette.size.toUInt()).toInt()
        return palette[index]
    }

    /** .NET's Guid.GetHashCode(): XOR of the GUID's four little-endian 32-bit words. */
    fun dotNetHashCode(id: UUID): Int {
        val msb = id.mostSignificantBits
        val lsb = id.leastSignificantBits
        val a = (msb ushr 32).toInt()
        val b = ((msb ushr 16) and 0xFFFF).toInt()
        val c = (msb and 0xFFFF).toInt()
        val word1 = b or (c shl 16)
        val word2 = Integer.reverseBytes((lsb ushr 32).toInt())
        val word3 = Integer.reverseBytes(lsb.toInt())
        return a xor word1 xor word2 xor word3
    }
}
