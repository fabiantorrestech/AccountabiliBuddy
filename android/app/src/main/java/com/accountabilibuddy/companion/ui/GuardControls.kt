package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.layout.RowScope
import androidx.compose.material3.Button as M3Button
import androidx.compose.material3.Checkbox as M3Checkbox
import androidx.compose.material3.FilterChip as M3FilterChip
import androidx.compose.material3.IconButton as M3IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton as M3OutlinedButton
import androidx.compose.material3.RadioButton as M3RadioButton
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton as M3TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.staticCompositionLocalOf
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.SpanStyle
import androidx.compose.ui.text.buildAnnotatedString
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.withStyle
import com.accountabilibuddy.companion.placement.GuardsLock
import com.accountabilibuddy.companion.ui.kit.AbInfoBanner

/**
 * The guard lock as the screens see it: set while a focus session runs (see [GuardsLock]), null otherwise. The shell provides it
 * from the ViewModel, which looks again every few seconds, so a page that is open unlocks by itself when the session ends.
 */
val LocalGuardsLock = staticCompositionLocalOf<GuardsLock?> { null }

/** True inside a guard section while the lock is on: its switches, chips and buttons draw as they are and take no taps. */
val LocalGuardsLocked = staticCompositionLocalOf { false }

/**
 * Wraps a guard section (Essentials, emergency time, placement, the phone guard switch, Curbox): while the lock is on, everything in
 * it that changes a setting is disabled. Wherever the section is shown (Guards, Sleep, Connections, the setup pages) it locks the same.
 */
@Composable
fun GuardsScope(content: @Composable () -> Unit) {
    CompositionLocalProvider(LocalGuardsLocked provides (LocalGuardsLock.current != null), content = content)
}

/** The banner on the Settings list and the Guards page while the lock is on: a lock, "Settings rest until **2:20 PM**." */
@Composable
fun GuardsRestBanner(lock: GuardsLock, modifier: Modifier = Modifier) {
    val time = lock.untilText()
    AbInfoBanner(
        buildAnnotatedString {
            append("Settings rest until ")
            if (time != null) withStyle(SpanStyle(fontWeight = FontWeight.Bold)) { append(time) } else append("your session ends")
            append(".")
        },
        modifier,
    )
}

/** The first line of a locked guard section: "Settings rest until 2:20 PM." Nothing while unlocked. */
@Composable
fun GuardsRestNote() {
    val lock = LocalGuardsLock.current ?: return
    Text(lock.sentence(), style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
}

// The guard sections use these under the names Button, OutlinedButton, ... (an import alias), so each respects the lock without a
// change at its call sites. Outside a [GuardsScope] they are the Material controls.

@Composable
fun GuardButton(onClick: () -> Unit, modifier: Modifier = Modifier, enabled: Boolean = true, content: @Composable RowScope.() -> Unit) =
    M3Button(onClick = onClick, modifier = modifier, enabled = enabled && !LocalGuardsLocked.current, content = content)

@Composable
fun GuardOutlinedButton(onClick: () -> Unit, modifier: Modifier = Modifier, enabled: Boolean = true, content: @Composable RowScope.() -> Unit) =
    M3OutlinedButton(onClick = onClick, modifier = modifier, enabled = enabled && !LocalGuardsLocked.current, content = content)

@Composable
fun GuardTextButton(onClick: () -> Unit, modifier: Modifier = Modifier, enabled: Boolean = true, content: @Composable RowScope.() -> Unit) =
    M3TextButton(onClick = onClick, modifier = modifier, enabled = enabled && !LocalGuardsLocked.current, content = content)

@Composable
fun GuardIconButton(onClick: () -> Unit, modifier: Modifier = Modifier, enabled: Boolean = true, content: @Composable () -> Unit) =
    M3IconButton(onClick = onClick, modifier = modifier, enabled = enabled && !LocalGuardsLocked.current, content = content)

@Composable
fun GuardFilterChip(selected: Boolean, onClick: () -> Unit, label: @Composable () -> Unit, modifier: Modifier = Modifier, enabled: Boolean = true) =
    M3FilterChip(selected = selected, onClick = onClick, label = label, modifier = modifier, enabled = enabled && !LocalGuardsLocked.current)

@Composable
fun GuardCheckbox(checked: Boolean, onCheckedChange: ((Boolean) -> Unit)?, modifier: Modifier = Modifier, enabled: Boolean = true) =
    M3Checkbox(checked = checked, onCheckedChange = onCheckedChange, modifier = modifier, enabled = enabled && !LocalGuardsLocked.current)

@Composable
fun GuardRadioButton(selected: Boolean, onClick: (() -> Unit)?, modifier: Modifier = Modifier, enabled: Boolean = true) =
    M3RadioButton(selected = selected, onClick = onClick, modifier = modifier, enabled = enabled && !LocalGuardsLocked.current)
