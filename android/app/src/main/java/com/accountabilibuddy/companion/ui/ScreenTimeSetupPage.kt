package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.ui.goals.UsageAccessRow
import com.accountabilibuddy.companion.ui.goals.rememberUsageAccess

/**
 * The migration assistant's page for screen-time goals (since 0.1.79, ST3): a goal can count several apps and sites, from the devices you
 * pick, and this phone reads its own screen time with Usage access (the same grant Essentials and Stats use; nothing new is asked). The row
 * shows whether it is on and opens the system's page. Nothing is turned on by itself; it is set up once Usage access is on, or seen here.
 */
@Composable
internal fun ScreenTimeSetupPage() {
    val context = LocalContext.current
    val on = rememberUsageAccess(context)
    Column(verticalArrangement = Arrangement.spacedBy(12.dp)) {
        Text("Goals: screen time", style = MaterialTheme.typography.headlineSmall)
        Text("A goal can count the time you spend in several apps and sites. Add the Screen time source in Edit goal > What counts, pick apps this phone has seen, and choose which of your devices count.")
        Text("This phone reads its own screen time with Usage access. Your usage data stays on the phone; only the minutes for each day go to your other devices. The apps and sites you list travel with the goal, sealed, to your own devices only.")
        UsageAccessRow(on, context)
    }
}
