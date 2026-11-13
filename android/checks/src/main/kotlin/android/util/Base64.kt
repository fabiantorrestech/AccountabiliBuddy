package android.util

/**
 * The checks' stand-in for Android's Base64 (a plain JVM program has none): only what the shared SyncKey calls, for a relay token
 * (URL-safe, no padding, no wrap). Nothing of the app ships this.
 */
object Base64 {
    const val NO_PADDING = 1
    const val NO_WRAP = 2
    const val URL_SAFE = 8

    fun encodeToString(input: ByteArray, flags: Int): String = java.util.Base64.getUrlEncoder().withoutPadding().encodeToString(input)
}
