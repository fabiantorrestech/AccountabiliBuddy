package com.accountabilibuddy.companion.essentials

import android.content.Context
import android.content.Intent
import android.util.Log

/**
 * Keeps the "Accountabilibuddy" profile in LockMyPhone-Extended in sync with the essentials (LockMyPhone-Extended's
 * ADR 0008): its allowed apps are the essentials, hidden ones hidden. The bedtime essentials keep a second one,
 * "Accountabilibuddy Bedtime", in its slot "bedtime" (its ADR 0010, LockMyPhone-Extended 0.13.0 on; an older one
 * would take it for the first profile, so it isn't sent). LockMyPhone-Extended only accepts this from an
 * app signed with its key (permission MANAGE_PROFILES, which it defines: install it before this app, or reinstall this
 * app after it). A lock that's already running doesn't change. Folders go along (with the row order) for LockMyPhone-Extended to show;
 * an essential allowed only in parts is allowed whole there (LockMyPhone-Extended can't tell screens apart).
 */
object ProfileSync {
    private const val TAG = "ProfileSync"

    private const val ACTION_SYNC = "${EssentialsLaunch.LOCK_MY_PHONE}.action.SYNC_PROFILE"

    // [{"name", "packages", "shown"}]: a folder's apps are also allowed; shown=false = found only by searching its name.
    private fun foldersJson(essentials: Essentials): String = kotlinx.serialization.json.buildJsonArray {
        essentials.folders.forEach { folder ->
            add(kotlinx.serialization.json.buildJsonObject {
                put("name", kotlinx.serialization.json.JsonPrimitive(folder.name))
                put("packages", kotlinx.serialization.json.JsonArray(folder.packages.map { kotlinx.serialization.json.JsonPrimitive(it) }))
                put("shown", kotlinx.serialization.json.JsonPrimitive(folder.shown))
            })
        }
    }.toString()

    // The essentials screen's rows in order, as "folder:<name>" and "app:<package>".
    private fun orderJson(essentials: Essentials): String {
        val names = essentials.folders.associate { Essentials.folderKey(it.id) to "folder:${it.name}" }
        val keys = essentials.homeKeys { it }.map { names[it] ?: it }
        return kotlinx.serialization.json.JsonArray(keys.map { kotlinx.serialization.json.JsonPrimitive(it) }).toString()
    }

    /** Sends [essentials] to LockMyPhone-Extended's profile for [kind], if it's installed (and new enough for a slot). */
    fun send(context: Context, essentials: Essentials, kind: EssentialsKind = EssentialsKind.Focus) {
        if (!EssentialsLaunch.lockMyPhoneInstalled(context)) return
        if (kind.lockMyPhoneSlot != null && !EssentialsLaunch.lockMyPhoneHasSlots(context)) return
        val intent = Intent(ACTION_SYNC)
            .setPackage(EssentialsLaunch.LOCK_MY_PHONE)
            .putExtra("manager", context.packageName)
            .putExtra("name", kind.lockMyPhoneName)
            .apply { kind.lockMyPhoneSlot?.let { putExtra("slot", it) } }
            .putExtra("allowed", essentials.apps.map { it.packageName }.toTypedArray())
            .putExtra("hidden", essentials.apps.filter { it.hidden }.map { it.packageName }.toTypedArray())
            // Folders and the row order (optional; an older LockMyPhone-Extended ignores them).
            .putExtra("folders", foldersJson(essentials))
            .putExtra("order", orderJson(essentials))
        runCatching { context.sendBroadcast(intent) }
            .onSuccess { Log.i(TAG, "sent ${essentials.apps.size} ${kind.name.lowercase()} essentials to LockMyPhone-Extended") }
            .onFailure { Log.w(TAG, "LockMyPhone-Extended not reached", it) }
    }
}
