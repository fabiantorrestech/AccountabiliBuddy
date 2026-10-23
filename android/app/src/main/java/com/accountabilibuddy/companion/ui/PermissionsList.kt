package com.accountabilibuddy.companion.ui

import android.Manifest
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.net.Uri
import android.os.Build
import android.os.PowerManager
import android.provider.Settings
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import androidx.core.app.NotificationManagerCompat
import androidx.core.content.ContextCompat
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.compose.LifecycleEventEffect
import com.accountabilibuddy.companion.nudge.Takeover
import com.accountabilibuddy.companion.usage.PhoneUsageReader

/** One permission the app works better with: whether it's on, why it's wanted, and how to turn it on. */
private data class PermissionRow(val title: String, val why: String, val granted: Boolean, val onAllow: () -> Unit)

/**
 * Every permission the phone app asks for, each with whether it's on and an Allow button that opens the right
 * screen: notifications, unrestricted battery, usage access, display over other apps, nearby devices (the car pause)
 * and Health Connect sleep. Checked again whenever the app comes back to the front. Used by Settings → Permissions and
 * the first-run setup.
 */
@Composable
fun PermissionsList(health: HealthStatus, onConnectHealth: () -> Unit, onAllowUsage: () -> Unit) {
    val context = LocalContext.current
    var checks by remember { mutableIntStateOf(0) }
    LifecycleEventEffect(Lifecycle.Event.ON_RESUME) { checks++ }
    val notifications = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) { checks++ }
    val nearby = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) { checks++ }

    val rows = remember(checks, health) {
        listOf(
            PermissionRow(
                "Notifications", "The timeline card, the work nudge, the parked phone and reminders.",
                NotificationManagerCompat.from(context).areNotificationsEnabled(),
            ) {
                if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) notifications.launch(Manifest.permission.POST_NOTIFICATIONS)
                else open(context, Intent(Settings.ACTION_APP_NOTIFICATION_SETTINGS).putExtra(Settings.EXTRA_APP_PACKAGE, context.packageName))
            },
            PermissionRow(
                "Unrestricted battery", "So Android doesn't put the app to sleep: nudges, sync and the pinned card stay on time.",
                context.getSystemService(PowerManager::class.java).isIgnoringBatteryOptimizations(context.packageName),
            ) {
                open(context, Intent(Settings.ACTION_REQUEST_IGNORE_BATTERY_OPTIMIZATIONS, Uri.parse("package:${context.packageName}")))
            },
            PermissionRow(
                "Usage access", "Phone screen time against your plan, and pickups (unlocks) during your computer's sessions.",
                PhoneUsageReader(context).hasAccess(), onAllowUsage,
            ),
            PermissionRow(
                "Display over other apps", "The full-screen work nudge when you unlock the phone during work time with no session.",
                Takeover.canShow(context),
            ) {
                open(context, Intent(Settings.ACTION_MANAGE_OVERLAY_PERMISSION, Uri.parse("package:${context.packageName}")))
            },
            PermissionRow(
                "Nearby devices", "Pause in the car: tells a car's Bluetooth from other devices (by its type, never its name).",
                Build.VERSION.SDK_INT < Build.VERSION_CODES.S ||
                    ContextCompat.checkSelfPermission(context, Manifest.permission.BLUETOOTH_CONNECT) == PackageManager.PERMISSION_GRANTED,
            ) {
                if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) nearby.launch(Manifest.permission.BLUETOOTH_CONNECT)
            },
            PermissionRow(
                "Health Connect sleep", "Real sleep from your watch or sleep app on the timeline.",
                health == HealthStatus.Connected || health == HealthStatus.NotAvailable,
                onConnectHealth,
            ),
        )
    }

    Column(verticalArrangement = Arrangement.spacedBy(10.dp)) {
        rows.forEach { row ->
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(10.dp), modifier = Modifier.fillMaxWidth()) {
                Text(if (row.granted) "✓" else "○", color = if (row.granted) Color(0xFF22C55E) else Color(0xFFF4A261), style = MaterialTheme.typography.titleMedium)
                Column(Modifier.weight(1f)) {
                    Text(row.title, style = MaterialTheme.typography.bodyLarge)
                    Text(row.why, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                }
                if (!row.granted) {
                    OutlinedButton(onClick = row.onAllow) { Text("Allow") }
                }
            }
        }
    }
}

/** How many of the permissions are on, e.g. "4 of 6 on". */
fun permissionsSummary(context: Context, health: HealthStatus): String {
    val on = listOf(
        NotificationManagerCompat.from(context).areNotificationsEnabled(),
        context.getSystemService(PowerManager::class.java).isIgnoringBatteryOptimizations(context.packageName),
        PhoneUsageReader(context).hasAccess(),
        Takeover.canShow(context),
        Build.VERSION.SDK_INT < Build.VERSION_CODES.S ||
            ContextCompat.checkSelfPermission(context, Manifest.permission.BLUETOOTH_CONNECT) == PackageManager.PERMISSION_GRANTED,
        health == HealthStatus.Connected || health == HealthStatus.NotAvailable,
    ).count { it }
    return "$on of 6 on"
}

private fun open(context: Context, intent: Intent) {
    runCatching { context.startActivity(intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)) }
        .onFailure { runCatching { context.startActivity(Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS, Uri.parse("package:${context.packageName}")).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)) } }
}
