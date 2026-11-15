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
 * the screen's Phone corner, icons and "only some screens" go along too (its ADR 0012; an older LockMyPhone-Extended allows
 * such an essential whole).
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

    // ADR 73 / LMP ADR 0012: the screen's own look, as that profile's overrides. "right"/"left"/"list" → LMP's PhoneButton.
    private fun phoneButton(corner: String): String = when (corner) {
        "left" -> "BOTTOM_LEFT"
        "list" -> "IN_LIST"
        else -> "BOTTOM_RIGHT"
    }

    // {"<package>": ["<activity class>", ...]} for essentials allowed only in some screens.
    private fun partsJson(essentials: Essentials): String = kotlinx.serialization.json.buildJsonObject {
        essentials.apps.forEach { app ->
            app.parts?.takeIf { it.isNotEmpty() }?.let { parts ->
                put(app.packageName, kotlinx.serialization.json.JsonArray(parts.map { kotlinx.serialization.json.JsonPrimitive(it) }))
            }
        }
    }.toString()

    /**
     * Sends [essentials] to LockMyPhone-Extended's profile for [kind], if it's installed (and new enough for a slot), with
     * the screen's Phone corner and icons ([phoneCorner], [showIcons]). LockMyPhone-Extended creates the profile when it
     * is missing, so the default lock always exists (ADR 73).
     */
    fun send(context: Context, essentials: Essentials, kind: EssentialsKind = EssentialsKind.Focus, phoneCorner: String = "right", showIcons: Boolean = true) {
        if (!EssentialsLaunch.lockMyPhoneInstalled(context)) return
        if (kind.lockMyPhoneSlot != null && !EssentialsLaunch.lockMyPhoneHasSlots(context)) return
        val intent = Intent(ACTION_SYNC)
            .putExtra("manager", context.packageName)
            .putExtra("name", kind.lockMyPhoneName)
            .apply { kind.lockMyPhoneSlot?.let { putExtra("slot", it) } }
            .putExtra("allowed", essentials.apps.map { it.packageName }.toTypedArray())
            .putExtra("hidden", essentials.apps.filter { it.hidden }.map { it.packageName }.toTypedArray())
            // Folders and the row order (optional; an older LockMyPhone-Extended ignores them).
            .putExtra("folders", foldersJson(essentials))
            .putExtra("order", orderJson(essentials))
            // The screen's own look and the "only some screens" apps (LMP ADR 0012; an older one ignores them and allows those apps whole).
            .putExtra("phone_button", phoneButton(phoneCorner))
            .putExtra("show_icons", showIcons)
            .putExtra("parts", partsJson(essentials))
        if (LockMyPhoneLink.send(context, intent)) Log.i(TAG, "sent ${essentials.apps.size} ${kind.name.lowercase()} essentials to LockMyPhone-Extended")
    }
}
