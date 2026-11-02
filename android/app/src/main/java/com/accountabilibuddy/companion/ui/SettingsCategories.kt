package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.ui.kit.AbTextButton
import com.accountabilibuddy.companion.ui.theme.Ab

/**
 * The phone's Settings pages (boards 5e and 6b): a list of these, each opening its own page. [key] is what follows
 * `settings/` in a route (`settings/devices`).
 */
enum class SettingsCategory(val key: String, val title: String, val icon: ImageVector) {
    YourDay("your-day", "Your day", SectionIcons.YourDay),
    Sleep("sleep", "Sleep", SectionIcons.Sleep),
    FocusSessions("focus-sessions", "Focus sessions", SectionIcons.FocusSessions),
    Guards("guards", "Guards", SectionIcons.Guards),
    Alerts("alerts", "Alerts & on-screen", SectionIcons.AlertsAndOnScreen),
    Devices("devices", "Devices & sync", SectionIcons.DevicesAndSync),
    Connections("connections", "Connections", SectionIcons.Connections),
    Appearance("appearance", "Appearance", SectionIcons.Appearance),
    AppAndData("app", "App & data", SectionIcons.AppAndData),
    Setup("setup", "Setup", SectionIcons.Setup),
    About("about", "About", SectionIcons.About);

    companion object {
        fun fromKey(key: String?): SettingsCategory? = entries.firstOrNull { it.key == key }

        /** The pages in the first card of the list; the rest sit below a gap. */
        val Main: List<SettingsCategory> = listOf(YourDay, Sleep, FocusSessions, Guards, Alerts, Devices, Connections, Appearance)
        val Below: List<SettingsCategory> = listOf(AppAndData, Setup, About)
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
            style = Ab.type.small,
            color = Ab.colors.waiting,
        )
        Row(horizontalArrangement = Arrangement.spacedBy(4.dp)) {
            missing.forEach { need -> need.fix?.let { fix -> AbTextButton("Set up ${need.label}", onClick = fix) } }
        }
    }
}
