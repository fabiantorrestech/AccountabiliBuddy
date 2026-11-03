package com.accountabilibuddy.companion.sync

import android.content.Context
import android.content.Intent
import com.accountabilibuddy.shared.TokenVault
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

/**
 * "Tell Autosync to sync": one explicit broadcast to Autosync's Automation receiver (https://metactrl.com/autosync/automation), with the
 * secret code the user set in Autosync. Off until set up. This app opens no connection: Autosync does its own syncing. A broadcast cannot
 * say whether Autosync got it, so [send] only says it was sent. The code is device-local: sealed by [TokenVault] in its own preferences
 * file ("autosyncNudge"), which is in no backup group and never in the sync file. Never logged.
 */
class AutosyncNudger(private val context: Context) {
    private val prefs = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
    private val _enabled = MutableStateFlow(prefs.getBoolean(KEY_ENABLED, false))
    private val _hasCode = MutableStateFlow(prefs.getString(KEY_CODE, null) != null)

    val policy = AutosyncNudgePolicy()

    /** The switch is on and a code is saved. */
    val enabled: StateFlow<Boolean> = _enabled.asStateFlow()

    val hasCode: StateFlow<Boolean> = _hasCode.asStateFlow()

    fun isReady(): Boolean = _enabled.value && _hasCode.value

    /** Saves the switch and, when [code] is not null, the secret code (an empty one removes it). */
    @Synchronized
    fun save(enabled: Boolean, code: String? = null) {
        val editor = prefs.edit().putBoolean(KEY_ENABLED, enabled)
        if (code != null) {
            val clean = code.trim()
            if (clean.isEmpty()) editor.remove(KEY_CODE) else editor.putString(KEY_CODE, TokenVault.seal(clean, TokenVault.AUTOSYNC_CODE_ALIAS))
            _hasCode.value = clean.isNotEmpty()
        }

        editor.apply()
        _enabled.value = enabled
    }

    fun isAutosyncInstalled(): Boolean =
        runCatching { context.packageManager.getPackageInfo(AUTOSYNC_PACKAGE, 0) }.isSuccess

    /** Sends the broadcast when ready (switch on, code saved); failures are swallowed. Returns whether it was sent. */
    fun sendIfReady(): Boolean = isReady() && send() == Result.Sent

    /** Sends the broadcast (also for the "Test" button, which works with the switch still off). */
    fun send(): Result {
        val code = prefs.getString(KEY_CODE, null)?.let { TokenVault.open(it, TokenVault.AUTOSYNC_CODE_ALIAS) }?.takeIf { it.isNotEmpty() } ?: return Result.NoCode
        if (!isAutosyncInstalled()) {
            return Result.NotInstalled
        }

        return try {
            val intent = Intent(ACTION)
                .setClassName(AUTOSYNC_PACKAGE, RECEIVER_CLASS)
                .addFlags(Intent.FLAG_INCLUDE_STOPPED_PACKAGES)
                .putExtra("secret", code)
            context.sendBroadcast(intent)
            Result.Sent
        } catch (e: Exception) {
            Result.Failed
        }
    }

    enum class Result { Sent, NoCode, NotInstalled, Failed }

    companion object {
        private const val PREFS = "autosyncNudge"
        private const val KEY_ENABLED = "enabled"
        private const val KEY_CODE = "codeSealed"
        const val AUTOSYNC_PACKAGE = "com.ttxapps.autosync"
        private const val RECEIVER_CLASS = "com.ttxapps.autosync.Automation"
        private const val ACTION = "syncNow"
    }
}
