package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.outlined.ArrowBack
import androidx.compose.material.icons.outlined.Bedtime
import androidx.compose.material.icons.outlined.Devices
import androidx.compose.material.icons.outlined.Headphones
import androidx.compose.material.icons.outlined.Notifications
import androidx.compose.material.icons.outlined.Security
import androidx.compose.material.icons.outlined.Settings
import androidx.compose.material.icons.outlined.Shield
import androidx.compose.material.icons.outlined.Today
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp

/** The phone's Settings categories: a list of these, each opening its own page. */
enum class SettingsCategory(val title: String, val icon: ImageVector) {
    Sleep("Sleep", Icons.Outlined.Bedtime),
    YourDay("Your day", Icons.Outlined.Today),
    PhoneGuard("Phone guard", Icons.Outlined.Shield),
    Devices("Devices & sync", Icons.Outlined.Devices),
    Notification("Notification & widget", Icons.Outlined.Notifications),
    Sound("Sound", Icons.Outlined.Headphones),
    Permissions("Permissions & setup", Icons.Outlined.Security),
    App("App", Icons.Outlined.Settings),
}

/** One category in the list: its icon, title and a one-line status; tap to open. */
@Composable
internal fun CategoryRow(category: SettingsCategory, summary: String, onOpen: () -> Unit) {
    Surface(
        shape = RoundedCornerShape(16.dp),
        color = MaterialTheme.colorScheme.surfaceContainerLow,
        modifier = Modifier.fillMaxWidth().clickable(onClick = onOpen),
    ) {
        Row(Modifier.padding(16.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(14.dp)) {
            Icon(category.icon, contentDescription = null, modifier = Modifier.size(26.dp), tint = MaterialTheme.colorScheme.primary)
            Column(Modifier.weight(1f)) {
                Text(category.title, style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.SemiBold)
                if (summary.isNotEmpty()) {
                    Text(summary, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                }
            }
            Text("›", style = MaterialTheme.typography.headlineSmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
        }
    }
}

/** A category page's header: back to the list and its title. */
@Composable
internal fun CategoryHeader(category: SettingsCategory, onBack: () -> Unit) {
    Row(verticalAlignment = Alignment.CenterVertically) {
        IconButton(onClick = onBack) { Icon(Icons.AutoMirrored.Outlined.ArrowBack, contentDescription = "Back to Settings") }
        Icon(category.icon, contentDescription = null, modifier = Modifier.size(22.dp), tint = MaterialTheme.colorScheme.primary)
        Text(category.title, style = MaterialTheme.typography.titleLarge, fontWeight = FontWeight.SemiBold, modifier = Modifier.padding(start = 10.dp))
    }
}

/** Something a feature needs first: what, whether it's there, and how to fix it. */
data class Need(val label: String, val ok: Boolean, val fix: (() -> Unit)? = null)

/**
 * "Needs: Health Connect ✓ · Usage access ✗ [Allow]" right on the feature, so a switch that can't work yet says why and
 * the missing piece is one tap away. Nothing shows when everything is there.
 */
@Composable
internal fun NeedsLine(needs: List<Need>, forWhat: String? = null) {
    val missing = needs.filter { !it.ok }
    if (missing.isEmpty()) return
    Column(verticalArrangement = Arrangement.spacedBy(2.dp)) {
        Text(
            (forWhat?.let { "$it needs: " } ?: "Needs: ") + needs.joinToString(" · ") { "${it.label} ${if (it.ok) "✓" else "✗"}" },
            style = MaterialTheme.typography.bodySmall,
            color = Color(0xFFFBBF24),
        )
        Row(horizontalArrangement = Arrangement.spacedBy(4.dp)) {
            missing.forEach { need -> need.fix?.let { fix -> TextButton(onClick = fix) { Text("Set up ${need.label}") } } }
        }
    }
}
