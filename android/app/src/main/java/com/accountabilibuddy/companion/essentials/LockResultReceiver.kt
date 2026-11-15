package com.accountabilibuddy.companion.essentials

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent

/**
 * LockMyPhone-Extended's answer to the session hold (LMP ADR 0011): started, held by another (stronger) lock,
 * refused, ended or released. The manifest guards it with the MANAGE_PROFILES signature permission, so only
 * LockMyPhone-Extended (signed with the same key) can send it.
 */
class LockResultReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        val holdId = intent.getStringExtra("hold_id") ?: return
        val event = intent.getStringExtra("event") ?: return
        SessionLock.onResult(context, holdId, event, intent.getStringExtra("lock_name"))
    }
}
