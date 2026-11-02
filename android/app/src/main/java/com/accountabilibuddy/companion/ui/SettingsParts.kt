package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.material.icons.outlined.ExpandLess
import androidx.compose.material.icons.outlined.ExpandMore
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp

// The pieces the Settings pages share: a folding titled group, a switch row and a folded explanation. Each page's own file is in
// ui/settings; the pages that were restyled in the redesign use the kit instead (Part 6b restyles the rest).

/**
 * A titled group. With a [summary] it folds: the title and the one-line summary show, the rest opens on a tap
 * ([startOpen] for the first look). Without one it's always open.
 */
@Composable
internal fun Section(title: String, summary: String? = null, startOpen: Boolean = false, content: @Composable () -> Unit) {
    var open by rememberSaveable(title) { mutableStateOf(summary == null || startOpen) }
    // Each section on its own tinted card, so one is easy to tell from the next.
    androidx.compose.material3.Surface(
        shape = androidx.compose.foundation.shape.RoundedCornerShape(16.dp),
        color = MaterialTheme.colorScheme.surfaceContainerLow,
        modifier = Modifier.fillMaxWidth(),
    ) {
    Column(Modifier.padding(horizontal = 16.dp, vertical = 14.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
        Row(
            verticalAlignment = Alignment.CenterVertically,
            modifier = Modifier.fillMaxWidth().clickable(enabled = summary != null) { open = !open },
        ) {
            Column(Modifier.weight(1f)) {
                IconTitle(title, style = MaterialTheme.typography.titleMedium)
                if (summary != null && !open) {
                    Text(summary, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant, maxLines = 1,
                        overflow = TextOverflow.Ellipsis)
                }
            }
            if (summary != null) {
                androidx.compose.material3.Icon(
                    if (open) androidx.compose.material.icons.Icons.Outlined.ExpandLess else androidx.compose.material.icons.Icons.Outlined.ExpandMore,
                    contentDescription = if (open) "Fold" else "Open",
                    tint = MaterialTheme.colorScheme.onSurfaceVariant,
                    modifier = Modifier.size(32.dp),
                )
            }
        }
        // Folds open and shut smoothly (Settings → Appearance → Animations; at once when off).
        val scale = LocalMotionScale.current
        androidx.compose.animation.AnimatedVisibility(
            visible = open,
            enter = androidx.compose.animation.expandVertically(motionSpec(scale, 220)) + androidx.compose.animation.fadeIn(motionSpec(scale, 220)),
            exit = androidx.compose.animation.shrinkVertically(motionSpec(scale, 180)) + androidx.compose.animation.fadeOut(motionSpec(scale, 180)),
        ) {
            Column(verticalArrangement = Arrangement.spacedBy(8.dp)) { content() }
        }
    }
    }
}

/** A switch with its title; a short [detail] shows under it, a long one behind ⓘ. */
@Composable
internal fun ToggleRow(title: String, detail: String, checked: Boolean, enabled: Boolean = true, onChange: (Boolean) -> Unit) {
    var explained by remember { mutableStateOf(false) }
    val long = detail.length > SHORT_TEXT
    Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.fillMaxWidth()) {
        Column(Modifier.weight(1f)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text(title, style = MaterialTheme.typography.bodyLarge)
                if (long) {
                    Text(" ⓘ", style = MaterialTheme.typography.bodyLarge, color = MaterialTheme.colorScheme.primary,
                        modifier = Modifier.clickable { explained = !explained })
                }
            }
            if (!long || explained) {
                Text(detail, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
        }
        // Inside a guard section while a focus session runs (the guard lock) a switch shows its value and takes no tap.
        Switch(checked = checked, onCheckedChange = onChange, enabled = enabled && !LocalGuardsLocked.current)
    }
}

/** An explanation folded behind "ⓘ [label]"; a tap shows the [text]. */
@Composable
internal fun Hint(text: String, label: String = "How this works") {
    var open by remember { mutableStateOf(false) }
    Column {
        Text("ⓘ $label", style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.primary,
            modifier = Modifier.clickable { open = !open }.padding(vertical = 2.dp))
        if (open) {
            Text(text, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
        }
    }
}

/** Longer than this, a switch's detail goes behind ⓘ. */
private const val SHORT_TEXT = 48
