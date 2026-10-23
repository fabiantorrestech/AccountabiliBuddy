package com.accountabilibuddy.companion.sync

import java.net.HttpURLConnection
import java.net.URL

/** One answer from Drive: the status code and the body. */
class DriveResponse(val code: Int, val body: ByteArray) {
    val isSuccess: Boolean get() = code in 200..299
}

/** Sends one request to Google Drive with a bearer token ([DriveNetwork], or a fake in checks). */
fun interface DriveTransport {
    fun send(method: String, url: String, token: String, contentType: String?, body: ByteArray?): DriveResponse
}

/**
 * The only place the phone app talks to the internet (ADR 26): Google Drive calls for the user's own sealed sync files,
 * with the drive.file scope (only files the app made). Google's sign-in itself runs inside Play services
 * ([DriveSignIn]). Requests carry the token and the sealed bytes, nothing else: no cookies, no identifying headers.
 */
object DriveNetwork : DriveTransport {
    private const val TIMEOUT_MS = 30_000

    // A sync file is a few KB; refuse anything much bigger rather than fill memory.
    private const val MAX_BYTES = 16 * 1024 * 1024

    override fun send(method: String, url: String, token: String, contentType: String?, body: ByteArray?): DriveResponse {
        val connection = URL(url).openConnection() as HttpURLConnection
        try {
            connection.connectTimeout = TIMEOUT_MS
            connection.readTimeout = TIMEOUT_MS
            connection.useCaches = false
            // HttpURLConnection has no PATCH; Google's APIs take the override header.
            if (method == "PATCH") {
                connection.requestMethod = "POST"
                connection.setRequestProperty("X-HTTP-Method-Override", "PATCH")
            } else {
                connection.requestMethod = method
            }

            connection.setRequestProperty("Authorization", "Bearer $token")
            if (body != null) {
                connection.doOutput = true
                contentType?.let { connection.setRequestProperty("Content-Type", it) }
                connection.setFixedLengthStreamingMode(body.size)
                connection.outputStream.use { it.write(body) }
            }

            val code = connection.responseCode
            val stream = if (code in 200..299) connection.inputStream else connection.errorStream
            val bytes = stream?.use { input ->
                val out = java.io.ByteArrayOutputStream()
                val buffer = ByteArray(8192)
                while (true) {
                    val count = input.read(buffer)
                    if (count < 0) break
                    out.write(buffer, 0, count)
                    if (out.size() > MAX_BYTES) throw java.io.IOException("Drive's answer is too big.")
                }
                out.toByteArray()
            } ?: ByteArray(0)
            return DriveResponse(code, bytes)
        } finally {
            connection.disconnect()
        }
    }
}
