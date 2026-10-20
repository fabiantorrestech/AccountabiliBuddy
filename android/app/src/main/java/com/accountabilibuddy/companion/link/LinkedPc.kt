package com.accountabilibuddy.companion.link

/** The PC this device is paired with: where to find it, the certificate to trust, and the sealed device token. */
data class LinkedPc(
    val pcName: String,
    val host: String,
    val port: Int,
    val fingerprint: String,
    val deviceId: String,
    val sealedToken: String,
)
