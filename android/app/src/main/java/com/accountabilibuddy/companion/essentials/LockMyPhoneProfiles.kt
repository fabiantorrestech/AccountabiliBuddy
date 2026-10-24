package com.accountabilibuddy.companion.essentials

import android.content.Context
import android.net.Uri
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

/**
 * LockMyPhone-Extended's profile names, for the hardcore profile picker: read from its profile list (a read-only
 * provider guarded by its signature permission MANAGE_PROFILES, the same as the profile sync). Names only.
 */
object LockMyPhoneProfiles {
    private val PROFILES: Uri = Uri.parse("content://${EssentialsLaunch.LOCK_MY_PHONE}.profiles/profiles")

    /** The names A–Z; null when LockMyPhone-Extended can't be asked (not installed, too old, or the permission is missing). */
    suspend fun names(context: Context): List<String>? = withContext(Dispatchers.IO) {
        runCatching {
            context.contentResolver.query(PROFILES, arrayOf("name"), null, null, null)?.use { cursor ->
                buildList { while (cursor.moveToNext()) cursor.getString(0)?.takeIf { it.isNotBlank() }?.let(::add) }
            }?.sortedBy { it.lowercase() }
        }.getOrNull()
    }
}
