package com.accountabilibuddy.companion.noise

import android.content.Context
import android.media.AudioManager
import android.os.Build
import android.os.Handler
import android.os.Looper
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.launch

/**
 * "Pause during calls": pauses the noise player while a call rings or is active and lets it resume afterwards (the
 * player resumes only what this paused). A call shows as Android's audio mode RINGTONE, IN_CALL or IN_COMMUNICATION, so
 * no phone permission is needed. Android 12 and later report mode changes; older versions are checked every few seconds,
 * only while the player is playing or waiting to resume. Runs while the app's process lives.
 */
class CallPauseWatcher(context: Context, private val player: NoisePlayer, private val scope: CoroutineScope) {
    private val audio = context.getSystemService(AudioManager::class.java)
    private val handler = Handler(Looper.getMainLooper())
    private var polling = false
    private var inCall = false

    private val poll = object : Runnable {
        override fun run() {
            onMode(audio.mode)
            // Stops by itself once nothing is playing and nothing waits to resume.
            if (polling) handler.postDelayed(this, POLL_MILLIS)
        }
    }

    fun start() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
            audio.addOnModeChangedListener(executor, ::onMode)
        } else {
            // Poll only while the player is playing; a call that ends while paused is seen on the next play or tick.
            scope.launch {
                player.state.map { it.playing }.distinctUntilChanged().collect { playing ->
                    if (playing) startPolling()
                }
            }
        }
    }

    private val executor = java.util.concurrent.Executor { handler.post(it) }

    private fun startPolling() {
        if (polling) return
        polling = true
        handler.post(poll)
    }

    private fun onMode(mode: Int) {
        val nowInCall = mode == AudioManager.MODE_RINGTONE || mode == AudioManager.MODE_IN_CALL || mode == AudioManager.MODE_IN_COMMUNICATION
        if (nowInCall && !inCall) {
            inCall = true
            player.callStarted()
        } else if (!nowInCall && inCall) {
            inCall = false
            player.callEnded()
        }

        // Older Android: keep checking only while playing or in a call (so the end is seen).
        if (polling && !inCall && !player.state.value.playing) {
            polling = false
            handler.removeCallbacks(poll)
        }
    }

    private companion object {
        const val POLL_MILLIS = 2500L
    }
}
