package com.accountabilibuddy.shared.sync

import android.util.Base64
import java.security.MessageDigest
import java.security.cert.CertificateException
import java.security.cert.X509Certificate
import javax.net.ssl.SSLContext
import javax.net.ssl.SSLSocketFactory
import javax.net.ssl.X509TrustManager

/**
 * Trusts exactly one certificate, whose SHA-256 (base64url) the user's own device gave: the pairing of the Wi-Fi link
 * used it (ADR 14, deleted by ADR 45), and the self-hosted relay (phase 6, ADR 46) reuses it. No authority is involved,
 * so another machine on the network can't pose as the PC.
 */
class PinnedTrustManager(private val fingerprint: String) : X509TrustManager {
    override fun checkServerTrusted(chain: Array<X509Certificate>, authType: String) {
        val certificate = chain.firstOrNull() ?: throw CertificateException("The PC sent no certificate.")
        val digest = MessageDigest.getInstance("SHA-256").digest(certificate.encoded)
        val actual = Base64.encodeToString(digest, Base64.URL_SAFE or Base64.NO_PADDING or Base64.NO_WRAP)
        if (!MessageDigest.isEqual(actual.toByteArray(), fingerprint.toByteArray())) {
            throw CertificateException("This isn't the paired PC (its certificate doesn't match).")
        }
    }

    override fun checkClientTrusted(chain: Array<X509Certificate>, authType: String) {
        throw CertificateException("The phone doesn't accept connections.")
    }

    override fun getAcceptedIssuers(): Array<X509Certificate> = emptyArray()

    fun socketFactory(): SSLSocketFactory =
        SSLContext.getInstance("TLS").apply { init(null, arrayOf(this@PinnedTrustManager), null) }.socketFactory
}
