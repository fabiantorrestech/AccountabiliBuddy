package com.accountabilibuddy.companion.essentials

import android.content.Context
import android.content.Intent
import android.util.Log

/**
 * Keeps the "Accountabilibuddy" profile in LockMyPhone-Extended in sync with the essentials (LockMyPhone-Extended's
 * ADR 0008): its allowed apps are the essentials, hidden ones hidden. LockMyPhone-Extended only accepts this from an
 * app signed with its key (permission MANAGE_PROFILES, which it defines: install it before this app, or reinstall this
 * app after it). A lock that's already running doesn't change. Folders' apps aren't essentials, so they aren't sent;
 * an essential allowed only in parts is allowed whole there (LockMyPhone-Extended can't tell screens apart).
 */
object ProfileSync {
    private const val TAG = "ProfileSync"

    /** The profile's name when LockMyPhone-Extended first creates it; renaming it there is kept. */
    const val PROFILE_NAME = "Accountabilibuddy"

    private const val ACTION_SYNC = "${EssentialsLaunch.LOCK_MY_PHONE}.action.SYNC_PROFILE"

    /** Sends [essentials] to LockMyPhone-Extended, if it's installed. */
    fun send(context: Context, essentials: Essentials) {
        if (!EssentialsLaunch.lockMyPhoneInstalled(context)) return
        val intent = Intent(ACTION_SYNC)
            .setPackage(EssentialsLaunch.LOCK_MY_PHONE)
            .putExtra("manager", context.packageName)
            .putExtra("name", PROFILE_NAME)
            .putExtra("allowed", essentials.apps.map { it.packageName }.toTypedArray())
            .putExtra("hidden", essentials.apps.filter { it.hidden }.map { it.packageName }.toTypedArray())
        runCatching { context.sendBroadcast(intent) }
            .onSuccess { Log.i(TAG, "sent ${essentials.apps.size} essentials to LockMyPhone-Extended") }
            .onFailure { Log.w(TAG, "LockMyPhone-Extended not reached", it) }
    }
}
