package com.accountabilibuddy.companion.noise

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import com.accountabilibuddy.companion.CompanionApp

/** The widget's volume − and + (a broadcast, so changing the volume never starts the player's foreground service). */
class NoiseVolumeReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        val noise = (context.applicationContext as CompanionApp).noise
        when (intent.action) {
            ACTION_DOWN -> noise.nudgeVolume(-STEP)
            ACTION_UP -> noise.nudgeVolume(STEP)
        }
    }

    companion object {
        const val ACTION_DOWN = "com.accountabilibuddy.companion.noise.VOLUME_DOWN"
        const val ACTION_UP = "com.accountabilibuddy.companion.noise.VOLUME_UP"
        private const val STEP = 10
    }
}
