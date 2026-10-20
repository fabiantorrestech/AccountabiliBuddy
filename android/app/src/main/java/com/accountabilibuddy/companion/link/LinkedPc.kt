package com.accountabilibuddy.companion.link

/**
 * The PC this device is paired with: where to find it, the certificate to trust, the sealed device token, and the
 * version of the PC's settings this phone already has (so the PC sends them only when they changed).
 */
data class LinkedPc(
    val pcName: String,
    val host: String,
    val port: Int,
    val fingerprint: String,
    val deviceId: String,
    val sealedToken: String,
    val settingsVersion: String? = null,
)
