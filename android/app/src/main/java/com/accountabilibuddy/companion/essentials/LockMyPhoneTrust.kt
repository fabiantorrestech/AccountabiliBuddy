package com.accountabilibuddy.companion.essentials

import android.content.Context
import android.content.pm.PackageInfo
import android.content.pm.PackageManager
import android.os.Build
import android.util.Log

/**
 * Whether the app on this phone with LockMyPhone-Extended's package name is the real one: signed with this app's own
 * certificate (the two share a signature permission, LMP ADR 0012; the installer applies the same rule to its download).
 * An app that only claims the package name is never used as the lock, never gets a broadcast and is never believed.
 */
object LockMyPhoneTrust {
    private const val TAG = "LockMyPhoneTrust"

    /** The signature permission both apps declare (LMP ADR 0012). */
    const val PERMISSION = "${EssentialsLaunch.LOCK_MY_PHONE}.permission.MANAGE_PROFILES"

    // The answer for one pair of installed versions: what is checked again only after either app is updated.
    private data class Verdict(val lmpUpdatedAt: Long, val ownUpdatedAt: Long, val ok: Boolean)

    @Volatile
    private var verdict: Verdict? = null

    /** True when something with LockMyPhone-Extended's package name is installed, whoever signed it. */
    fun present(context: Context): Boolean = runCatching { context.packageManager.getPackageInfo(EssentialsLaunch.LOCK_MY_PHONE, 0) }.isSuccess

    /** True when the installed LockMyPhone-Extended is signed with this app's certificate. */
    fun verified(context: Context): Boolean {
        val pm = context.packageManager
        val lmp = runCatching { pm.getPackageInfo(EssentialsLaunch.LOCK_MY_PHONE, 0) }.getOrNull() ?: return false
        val own = runCatching { pm.getPackageInfo(context.packageName, 0) }.getOrNull() ?: return false
        verdict?.takeIf { it.lmpUpdatedAt == lmp.lastUpdateTime && it.ownUpdatedAt == own.lastUpdateTime }?.let { return it.ok }
        val ok = runCatching {
            SigningCertificates.matches(certificates(packageInfo(pm, EssentialsLaunch.LOCK_MY_PHONE)), certificates(packageInfo(pm, context.packageName)))
        }.getOrDefault(false)
        if (!ok) Log.w(TAG, "the app named ${EssentialsLaunch.LOCK_MY_PHONE} isn't signed with this app's key: not used")
        verdict = Verdict(lmp.lastUpdateTime, own.lastUpdateTime, ok)
        return ok
    }

    /** True when the installed LockMyPhone-Extended holds the signature permission, so a broadcast guarded by it reaches it. */
    fun holdsPermission(context: Context): Boolean =
        context.packageManager.checkPermission(PERMISSION, EssentialsLaunch.LOCK_MY_PHONE) == PackageManager.PERMISSION_GRANTED

    fun packageInfo(pm: PackageManager, packageName: String): PackageInfo =
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.P) {
            pm.getPackageInfo(packageName, PackageManager.GET_SIGNING_CERTIFICATES)
        } else {
            @Suppress("DEPRECATION")
            pm.getPackageInfo(packageName, PackageManager.GET_SIGNATURES)
        }

    /** The SHA-256 of each signing certificate of [info]. */
    fun certificates(info: PackageInfo): Set<String> {
        val signatures = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.P) {
            info.signingInfo?.apkContentsSigners
        } else {
            @Suppress("DEPRECATION")
            info.signatures
        }
        return signatures.orEmpty().map { SigningCertificates.sha256Hex(it.toByteArray()) }.toSet()
    }
}
