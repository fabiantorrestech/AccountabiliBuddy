package com.accountabilibuddy.companion.ui

import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.staticCompositionLocalOf
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.SpanStyle
import androidx.compose.ui.text.buildAnnotatedString
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.withStyle
import com.accountabilibuddy.companion.placement.GuardsLock
import com.accountabilibuddy.companion.ui.kit.AbChip
import com.accountabilibuddy.companion.ui.kit.AbChoiceRow
import com.accountabilibuddy.companion.ui.kit.AbInfoBanner
import com.accountabilibuddy.companion.ui.kit.AbTextButton
import com.accountabilibuddy.companion.ui.kit.AbTonalButton
import com.accountabilibuddy.companion.ui.kit.AbValueButton
import com.accountabilibuddy.companion.ui.theme.Ab

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
    Text(lock.sentence(), style = Ab.type.small, color = Ab.colors.muted)
}

// The guard sections use these kit controls: each is the kit's own with the lock added, so a section respects it without a check at
// its call sites. Outside a [GuardsScope] they are the kit's controls as they are.

/** A secondary button ([AbTonalButton]) that takes no tap while the guards rest. */
@Composable
fun GuardTonalButton(text: String, onClick: () -> Unit, modifier: Modifier = Modifier, enabled: Boolean = true) =
    AbTonalButton(text, onClick, modifier, enabled = enabled && !LocalGuardsLocked.current)

/** A small outlined button ([AbValueButton]) that takes no tap while the guards rest. */
@Composable
fun GuardValueButton(text: String, onClick: () -> Unit, modifier: Modifier = Modifier, enabled: Boolean = true) =
    AbValueButton(text, onClick, modifier, enabled = enabled && !LocalGuardsLocked.current)

/** A quiet text button ([AbTextButton]) that takes no tap while the guards rest. */
@Composable
fun GuardTextButton(text: String, onClick: () -> Unit, modifier: Modifier = Modifier, enabled: Boolean = true) =
    AbTextButton(text, onClick, modifier, enabled = enabled && !LocalGuardsLocked.current)

/** A chip ([AbChip]) that takes no tap while the guards rest. */
@Composable
fun GuardChip(label: String, selected: Boolean, onClick: () -> Unit, modifier: Modifier = Modifier, enabled: Boolean = true) =
    AbChip(label, selected, onClick, modifier, enabled = enabled && !LocalGuardsLocked.current)

/** One option of a choice ([AbChoiceRow]) that takes no tap while the guards rest. */
@Composable
fun GuardChoiceRow(text: String, selected: Boolean, modifier: Modifier = Modifier, subtitle: String? = null, enabled: Boolean = true, onClick: () -> Unit) =
    AbChoiceRow(text, selected, modifier, subtitle, enabled = enabled && !LocalGuardsLocked.current, onClick = onClick)
