package com.accountabilibuddy.companion.ui

import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.platform.LocalContext
import com.accountabilibuddy.companion.notify.StartOnBoot

/** Settings → Startup: start when the phone turns on (on by default). */
@Composable
internal fun StartupSection() {
    val context = LocalContext.current
    var on by remember { mutableStateOf(StartOnBoot.isOn(context)) }
    Section("Startup", summary = if (on) "Starts with the phone" else "Starts when opened") {
        ToggleRow(
            "Start when the phone turns on",
            "Pins the timeline card again after a restart, so the work nudges, the work card and syncing keep going without opening the app.",
            on,
        ) {
            StartOnBoot.set(context, it)
            on = it
        }
    }
}
