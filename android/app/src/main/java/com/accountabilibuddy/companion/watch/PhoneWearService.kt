package com.accountabilibuddy.companion.watch

import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.shared.WatchPaths
import com.google.android.gms.wearable.MessageEvent
import com.google.android.gms.wearable.WearableListenerService
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.withTimeoutOrNull

/** Wakes the phone app when the watch app asks for news ("/hello", ADR 21), even when the phone app isn't running. */
class PhoneWearService : WearableListenerService() {
    override fun onMessageReceived(event: MessageEvent) {
        if (event.path != WatchPaths.HELLO) {
            return
        }

        // Called on a background thread; the answer may wait a few seconds for the PC.
        val bridge = (application as CompanionApp).watchBridge
        runBlocking {
            withTimeoutOrNull(ANSWER_LIMIT_MS) { bridge.answerHello(event.sourceNodeId) }
        }
    }

    private companion object {
        const val ANSWER_LIMIT_MS = 15_000L
    }
}
