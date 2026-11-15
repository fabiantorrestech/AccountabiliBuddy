package com.accountabilibuddy.companion.essentials

import android.content.Context
import android.content.Intent
import android.net.Uri
import android.util.Log

/** A Morning lock as LockMyPhone-Extended holds it for this app (LMP ADR 0013). */
data class MorningLockState(
    val enabled: Boolean,
    val beforeAlarm: Boolean,
    val beforeAlarmMinutes: Int,
    val noAlarmFallback: Boolean,
    val lockMinutes: Int,
    val profile: String?,
)

/**
 * LockMyPhone-Extended's companion contract, version 2 (LMP ADR 0011-0013): its capabilities, the session hold and
 * the Morning lock. Every call is an on-device broadcast or query guarded by the MANAGE_PROFILES signature
 * permission, which this app declares too (LMP ADR 0012); nothing leaves the phone.
 */
object LockMyPhoneClient {
    private const val TAG = "LockMyPhoneClient"
    private const val PACKAGE = EssentialsLaunch.LOCK_MY_PHONE
    private val CAPABILITIES: Uri = Uri.parse("content://$PACKAGE.profiles/capabilities")
    private val MORNING_LOCK: Uri = Uri.parse("content://$PACKAGE.profiles/morning_lock")
    private const val ACTION_HOLD = "$PACKAGE.action.HOLD_LOCK"
    private const val ACTION_RELEASE = "$PACKAGE.action.RELEASE_LOCK"
    private const val ACTION_SET_MORNING_LOCK = "$PACKAGE.action.SET_MORNING_LOCK"

    const val HOLD_LOCK = "HOLD_LOCK"
    const val SYNC_SCREEN_OPTIONS = "SYNC_SCREEN_OPTIONS"
    const val SET_MORNING_LOCK = "SET_MORNING_LOCK"

    /** The contract version this app needs for everything in ADR 73; an older LockMyPhone-Extended is offered an update. */
    const val NEEDED_CONTRACT_VERSION = 2

    // The last answer, for a minute: it's asked on every unlock and refresh, and only changes when the app is updated.
    private var cached: Pair<Int, Set<String>>? = null
    private var cachedAtMs = 0L
    private const val CACHE_MS = 60_000L

    /** The installed LockMyPhone-Extended's contract version and actions; null when it can't be asked (missing, too old, no permission). */
    @Synchronized
    fun capabilities(context: Context): Pair<Int, Set<String>>? {
        val now = System.currentTimeMillis()
        if (now - cachedAtMs < CACHE_MS) return cached
        cached = runCatching {
            context.contentResolver.query(CAPABILITIES, null, null, null, null)?.use { cursor ->
                if (!cursor.moveToFirst()) return@use null
                val version = cursor.getInt(cursor.getColumnIndexOrThrow("contract_version"))
                val actions = cursor.getString(cursor.getColumnIndexOrThrow("actions")).orEmpty().split(',').map { it.trim() }.filter { it.isNotEmpty() }.toSet()
                version to actions
            }
        }.getOrNull()
        cachedAtMs = now
        return cached
    }

    /** Forgets the cached capabilities (after LockMyPhone-Extended was installed or updated). */
    @Synchronized
    fun forgetCapabilities() {
        cachedAtMs = 0L
    }

    /** True when the installed LockMyPhone-Extended accepts [action]. */
    fun supports(context: Context, action: String): Boolean = capabilities(context)?.second?.contains(action) == true

    /** True when LockMyPhone-Extended is installed but older than this app needs (the Update button). */
    fun needsUpdate(context: Context): Boolean =
        EssentialsLaunch.lockMyPhoneInstalled(context) && (capabilities(context)?.first ?: 0) < NEEDED_CONTRACT_VERSION

    /** Holds the phone locked with [profile] until [untilMs] for [holdId]; sent again, it moves the end. */
    fun hold(context: Context, holdId: String, profile: String, untilMs: Long): Boolean = send(
        context,
        Intent(ACTION_HOLD).putExtra("hold_id", holdId).putExtra("profile", profile).putExtra("until_ms", untilMs),
    )

    /** Drops [holdId]'s hold; LockMyPhone-Extended ends only the lock the hold started. */
    fun release(context: Context, holdId: String): Boolean = send(context, Intent(ACTION_RELEASE).putExtra("hold_id", holdId))

    /** Sets this app's own Morning lock in LockMyPhone-Extended (it stays editable there; the last change wins). */
    fun setMorningLock(context: Context, state: MorningLockState): Boolean = send(
        context,
        Intent(ACTION_SET_MORNING_LOCK)
            .putExtra("enabled", state.enabled)
            .putExtra("start_mode", if (state.beforeAlarm) "BEFORE_ALARM" else "WAKE_UP")
            .putExtra("before_alarm_minutes", state.beforeAlarmMinutes)
            .putExtra("no_alarm_fallback", state.noAlarmFallback)
            .putExtra("lock_minutes", state.lockMinutes)
            .apply { state.profile?.let { putExtra("profile", it) } },
    )

    /** This app's Morning lock as LockMyPhone-Extended holds it now; null when there is none or it can't be asked. */
    fun morningLock(context: Context): MorningLockState? = runCatching {
        context.contentResolver.query(MORNING_LOCK, null, null, null, null)?.use { cursor ->
            if (!cursor.moveToFirst()) return@use null
            MorningLockState(
                enabled = cursor.getInt(cursor.getColumnIndexOrThrow("enabled")) == 1,
                beforeAlarm = cursor.getString(cursor.getColumnIndexOrThrow("start_mode")) == "BEFORE_ALARM",
                beforeAlarmMinutes = cursor.getInt(cursor.getColumnIndexOrThrow("before_alarm_minutes")),
                noAlarmFallback = cursor.getInt(cursor.getColumnIndexOrThrow("no_alarm_fallback")) == 1,
                lockMinutes = cursor.getInt(cursor.getColumnIndexOrThrow("lock_minutes")),
                profile = cursor.getString(cursor.getColumnIndexOrThrow("profile")),
            )
        }
    }.getOrNull()

    private fun send(context: Context, intent: Intent): Boolean {
        intent.setPackage(PACKAGE).putExtra("manager", context.packageName)
        return runCatching { context.sendBroadcast(intent) }
            .onFailure { Log.w(TAG, "LockMyPhone-Extended not reached (${intent.action})", it) }
            .isSuccess
    }
}
