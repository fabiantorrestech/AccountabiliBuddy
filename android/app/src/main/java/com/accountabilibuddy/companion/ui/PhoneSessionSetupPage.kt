package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.unit.dp

/**
 * The migration assistant's page for focus sessions on this phone (since 0.1.80, ADR 85, PS3a). It explains; nothing is asked or changed, every step is optional,
 * and a phone with no PC reads the same (R15). A session started once, or a profile made, marks it set up by itself.
 */
@Composable
internal fun PhoneSessionSetupPage() {
    Column(verticalArrangement = Arrangement.spacedBy(12.dp)) {
        Text("Focus sessions on this phone", style = MaterialTheme.typography.headlineSmall)
        Text("Start has a choice now: \"On <your PC>\" or \"On this phone\". With no PC in your group, only \"On this phone\" is offered, and nothing is sent anywhere.")
        Text("The phone remembers your choice. When your PC is offline, Start opens on this phone and says when the PC was last heard from.")
        Text("A session on this phone counts in Today, Stats, your goals and Deep work, and only one session runs at a time across your devices. A session on your PC or another phone shows on Today as \"Running on …\".")
        Text("In AccountabiliBuddy mode, ending early takes a short wait and a reason, and Settings and your plan rest while it runs. In ProductaBuddy mode you can end it any time and it locks only what its profile turns on.")
        Text("The built-in Focus profile is a plain timer with This session's tasks. Session profiles that also turn on LockMyPhone-Extended or Curbox come to Settings next.", color = MaterialTheme.colorScheme.onSurfaceVariant)
    }
}
