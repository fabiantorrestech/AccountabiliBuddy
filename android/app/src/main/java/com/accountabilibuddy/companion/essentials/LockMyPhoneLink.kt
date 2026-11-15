package com.accountabilibuddy.companion.essentials

import android.content.Context
import android.content.Intent
import android.util.Log

/**
 * Every broadcast to LockMyPhone-Extended goes through here: only when the installed app is the verified one
 * ([LockMyPhoneTrust.verified]), addressed to its package, and, when it holds the signature permission (contract v2,
 * LMP ADR 0012), guarded by it, so no other app can receive it. An older LockMyPhone-Extended without the permission
 * still gets the broadcast (it is the verified app, addressed by package), exactly as before.
 */
object LockMyPhoneLink {
    private const val TAG = "LockMyPhoneLink"

    /** Sends [intent] to the real LockMyPhone-Extended; false when it isn't installed and verified, or the send failed. */
    fun send(context: Context, intent: Intent): Boolean {
        if (!EssentialsLaunch.lockMyPhoneInstalled(context)) return false
        intent.setPackage(EssentialsLaunch.LOCK_MY_PHONE)
        val permission = LockMyPhoneTrust.PERMISSION.takeIf { LockMyPhoneTrust.holdsPermission(context) }
        return runCatching { context.sendBroadcast(intent, permission) }
            .onFailure { Log.w(TAG, "LockMyPhone-Extended not reached (${intent.action})", it) }
            .isSuccess
    }
}
