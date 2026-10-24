package com.accountabilibuddy.companion.nudge

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.ui.CompanionTheme

/** "Need my phone" from the parked notice during a session on another device: the emergency chooser on its own. */
class EmergencyActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        val app = application as CompanionApp
        setContent {
            CompanionTheme {
                Box(Modifier.fillMaxSize().background(Color(0xFF0B1F1E)).padding(28.dp), contentAlignment = Alignment.Center) {
                    EmergencyChooser(app.emergency, onStarted = {
                        app.refreshSoon()
                        finish()
                    }, onBack = { finish() })
                }
            }
        }
    }
}
