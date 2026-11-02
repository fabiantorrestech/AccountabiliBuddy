package com.accountabilibuddy.companion.debug

import android.app.Activity
import android.content.Intent
import android.os.Bundle
import android.speech.RecognizerIntent

/**
 * Debug builds only: stands in for the phone's voice service on an emulator that has none, so the Park pill's microphone can be
 * tried and photographed. Disabled until [DebugVoiceSwitch] turns it on; it
 * answers the system speech request at once with a made-up sentence.
 */
class DebugVoiceActivity : Activity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        val heard = arrayListOf("Ask Sam about the API key before Friday")
        setResult(RESULT_OK, Intent().putStringArrayListExtra(RecognizerIntent.EXTRA_RESULTS, heard))
        finish()
    }
}
