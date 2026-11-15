package com.accountabilibuddy.companion.essentials

import android.content.Context
import android.content.Intent
import android.content.pm.PackageInfo
import android.content.pm.PackageManager
import android.net.Uri
import android.os.Build
import android.provider.Settings
import android.util.Log
import androidx.core.content.FileProvider
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.io.File
import java.net.HttpURLConnection
import java.net.URL
import java.security.MessageDigest

/**
 * Downloads and installs LockMyPhone-Extended from the owner's public releases (ADR 74). The phone's one connection
 * for it: only on a tap of Download or Update, an anonymous GET of [APK_URL] with a generic user agent, nothing sent.
 *
 * The file goes into the app's cache. It is handed to Android's installer only when its package is LockMyPhone-Extended's
 * and it is signed with this app's own certificate (the two share a signature permission, LMP ADR 0012). Android still
 * asks the user to allow installing and to confirm.
 */
object LockMyPhoneInstaller {
    private const val TAG = "LockMyPhoneInstaller"

    /** The release asset (ADR 74). */
    const val APK_URL = "https://github.com/fabiantorrestech/android-apps/releases/download/lockmyphoneextended/LockMyPhoneExtended.apk"

    /** The owner's apps page and LockMyPhone-Extended's source, opened in the browser (not connections of this app). */
    const val APPS_PAGE_URL = "https://fabiantorrestech.github.io/android-apps/"
    const val SOURCE_URL = "https://github.com/fabiantorrestech/LockMyPhone-Extended"

    private const val FOLDER = "lockmyphone"
    private const val FILE_NAME = "LockMyPhoneExtended.apk"
    private const val MAX_BYTES = 80L * 1024 * 1024
    private const val TIMEOUT_MS = 20_000

    /** Why a download can't be installed, for the user. */
    sealed interface Outcome {
        data object Ready : Outcome
        data class Failed(val message: String) : Outcome
    }

    /** The downloaded installer, if one is still in the cache. */
    fun downloaded(context: Context): File? = file(context).takeIf { it.isFile }

    /** Deletes the downloaded installer (asked after LockMyPhone-Extended was installed). */
    fun deleteDownloaded(context: Context): Boolean = file(context).delete()

    /** Downloads the latest LockMyPhone-Extended and checks it. Call on a tap only. */
    suspend fun download(context: Context): Outcome = withContext(Dispatchers.IO) {
        val target = file(context)
        target.parentFile?.mkdirs()
        val partial = File(target.parentFile, "$FILE_NAME.part")
        val result = runCatching {
            val connection = (URL(APK_URL).openConnection() as HttpURLConnection).apply {
                connectTimeout = TIMEOUT_MS
                readTimeout = TIMEOUT_MS
                instanceFollowRedirects = true
                setRequestProperty("User-Agent", "AccountabiliBuddy-Android")
            }
            try {
                if (connection.responseCode != HttpURLConnection.HTTP_OK) error("The download answered ${connection.responseCode}")
                connection.inputStream.use { input ->
                    partial.outputStream().use { output ->
                        val buffer = ByteArray(64 * 1024)
                        var total = 0L
                        while (true) {
                            val read = input.read(buffer)
                            if (read < 0) break
                            total += read
                            if (total > MAX_BYTES) error("The download is larger than expected")
                            output.write(buffer, 0, read)
                        }
                    }
                }
            } finally {
                connection.disconnect()
            }
        }
        result.exceptionOrNull()?.let {
            partial.delete()
            Log.w(TAG, "download failed", it)
            return@withContext Outcome.Failed("Couldn't download LockMyPhone-Extended: ${it.message ?: "no connection"}.")
        }
        val problem = check(context, partial)
        if (problem != null) {
            partial.delete()
            return@withContext Outcome.Failed(problem)
        }
        target.delete()
        partial.renameTo(target)
        Outcome.Ready
    }

    /**
     * Opens Android's installer for the downloaded file. When this app may not install apps yet, opens Android's
     * "Install unknown apps" page for it instead and returns false (tap Install again after allowing it).
     */
    fun install(context: Context): Boolean {
        val apk = downloaded(context) ?: return false
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O && !context.packageManager.canRequestPackageInstalls()) {
            context.startActivity(
                Intent(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES, Uri.parse("package:${context.packageName}"))
                    .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK),
            )
            return false
        }
        val uri = FileProvider.getUriForFile(context, "${context.packageName}.files", apk)
        context.startActivity(
            Intent(Intent.ACTION_VIEW)
                .setDataAndType(uri, "application/vnd.android.package-archive")
                .addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION or Intent.FLAG_ACTIVITY_NEW_TASK),
        )
        LockMyPhoneClient.forgetCapabilities()
        return true
    }

    /** Null when [apk] is LockMyPhone-Extended signed with this app's certificate; else why it can't be installed. */
    private fun check(context: Context, apk: File): String? {
        val pm = context.packageManager
        val archive = archiveInfo(pm, apk) ?: return "The download isn't an app that can be installed."
        if (archive.packageName != EssentialsLaunch.LOCK_MY_PHONE) return "The download isn't LockMyPhone-Extended."
        val own = runCatching { packageInfo(pm, context.packageName) }.getOrNull() ?: return "This app's own signature couldn't be read."
        val theirs = certificates(archive)
        if (theirs.isEmpty() || theirs != certificates(own)) {
            return "The download isn't signed with this app's key, so it wouldn't work with it. Install it from the apps page instead."
        }
        return null
    }

    private fun archiveInfo(pm: PackageManager, apk: File): PackageInfo? =
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.P) {
            pm.getPackageArchiveInfo(apk.path, PackageManager.GET_SIGNING_CERTIFICATES)
        } else {
            @Suppress("DEPRECATION")
            pm.getPackageArchiveInfo(apk.path, PackageManager.GET_SIGNATURES)
        }

    private fun packageInfo(pm: PackageManager, packageName: String): PackageInfo =
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.P) {
            pm.getPackageInfo(packageName, PackageManager.GET_SIGNING_CERTIFICATES)
        } else {
            @Suppress("DEPRECATION")
            pm.getPackageInfo(packageName, PackageManager.GET_SIGNATURES)
        }

    // The SHA-256 of each signing certificate.
    private fun certificates(info: PackageInfo): Set<String> {
        val signatures = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.P) {
            info.signingInfo?.apkContentsSigners
        } else {
            @Suppress("DEPRECATION")
            info.signatures
        }
        val digest = MessageDigest.getInstance("SHA-256")
        return signatures.orEmpty().map { sig -> digest.digest(sig.toByteArray()).joinToString("") { "%02x".format(it) } }.toSet()
    }

    private fun file(context: Context): File = File(File(context.cacheDir, FOLDER), FILE_NAME)
}
