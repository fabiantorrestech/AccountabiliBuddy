package com.accountabilibuddy.companion.placement

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
import com.accountabilibuddy.companion.essentials.EssentialsActivity
import com.accountabilibuddy.companion.ui.CompanionTheme

/** "Need it for work" from the parked notice or Today during a session: the work use chooser on its own. */
class WorkUseActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        val app = application as CompanionApp
        setContent {
            CompanionTheme {
                Box(Modifier.fillMaxSize().background(Color(0xFF0B1F1E)).padding(28.dp), contentAlignment = Alignment.Center) {
                    WorkUseChooser(app, onStarted = {
                        // As after "I need my phone for a bit": on to the essentials screen.
                        startActivity(EssentialsActivity.intent(this@WorkUseActivity))
                        finish()
                    }, onBack = { finish() })
                }
            }
        }
    }
}
