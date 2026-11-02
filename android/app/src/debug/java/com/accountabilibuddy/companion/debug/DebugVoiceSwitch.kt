package com.accountabilibuddy.companion.debug

import android.content.BroadcastReceiver
import android.content.ComponentName
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager

/**
 * Debug builds only: turns [DebugVoiceActivity] on or off from adb (the shell may not change a component's state itself):
 * `adb -s emulator-5554 shell am broadcast -a com.accountabilibuddy.companion.debug.VOICE -p com.accountabilibuddy.companion --ez on true`.
 */
class DebugVoiceSwitch : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        val on = intent.getBooleanExtra("on", false)
        context.packageManager.setComponentEnabledSetting(
            ComponentName(context, DebugVoiceActivity::class.java),
            if (on) PackageManager.COMPONENT_ENABLED_STATE_ENABLED else PackageManager.COMPONENT_ENABLED_STATE_DISABLED,
            PackageManager.DONT_KILL_APP,
        )
    }
}
