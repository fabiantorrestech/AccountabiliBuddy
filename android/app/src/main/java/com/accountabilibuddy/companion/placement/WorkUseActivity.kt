package com.accountabilibuddy.companion.placement

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.ui.Alignment
import com.accountabilibuddy.companion.ui.setAbContent
import com.accountabilibuddy.companion.ui.shell.GuardFrame
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.ui.CompanionTheme

/** "Need it for work" from the parked notice or Today during a session: the work use chooser on its own. */
class WorkUseActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        val app = application as CompanionApp
        setAbContent {
            CompanionTheme {
                GuardFrame(Modifier.fillMaxSize().background(Color(0xFF0B1F1E)).padding(28.dp), contentAlignment = Alignment.Center) {
                    WorkUseChooser(app, onStarted = {
                        // ADR 73: "Need it for work" leaves the phone free for its minutes (no screen; the session's lock lifts).
                        com.accountabilibuddy.companion.essentials.SessionLock.reconcile(this@WorkUseActivity)
                        finish()
                    }, onBack = { finish() })
                }
            }
        }
    }
}
