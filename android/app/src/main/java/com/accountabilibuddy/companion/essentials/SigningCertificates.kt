package com.accountabilibuddy.companion.essentials

import java.security.MessageDigest

/** The certificate rule shared by the installer's download check and the "is this the real LockMyPhone-Extended" check (ADR 74). Pure. */
object SigningCertificates {
    /** The lowercase hex SHA-256 of one signing certificate. */
    fun sha256Hex(certificate: ByteArray): String =
        MessageDigest.getInstance("SHA-256").digest(certificate).joinToString("") { "%02x".format(it) }

    /** True only when [theirs] has at least one certificate and is exactly [own]. */
    fun matches(theirs: Set<String>, own: Set<String>): Boolean = theirs.isNotEmpty() && theirs == own
}
