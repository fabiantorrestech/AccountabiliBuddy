package com.accountabilibuddy.companion.ui.today

import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.MusicNote
import androidx.compose.material.icons.outlined.Tune
import androidx.compose.material.icons.outlined.ViewStream
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import com.accountabilibuddy.companion.ui.UiPrefs
import com.accountabilibuddy.companion.ui.kit.AbSegmentedCompact
import com.accountabilibuddy.companion.ui.sheets.AbSheet
import com.accountabilibuddy.companion.ui.sheets.AbSheetHeight
import com.accountabilibuddy.companion.ui.sheets.AbSheetRow
import com.accountabilibuddy.companion.ui.sheets.AbSheetValue

/**
 * The header's ⋮ on the phone's Today: Today's options, as the PC's Today Options menu (R2.2). Today layout opens Settings › Today's
 * editor (two layouts); Noise opens the Noise picker once a sound folder is set ([noiseTitle] non-null).
 */
@Composable
fun TodayOptionsSheet(noiseTitle: String?, onArrange: () -> Unit, onNoise: () -> Unit, onDismiss: () -> Unit) {
    AbSheet(title = "Today", subtitle = "This phone only", onDismiss = onDismiss, height = AbSheetHeight.Half) {
        AbSheetRow(
            title = "Today settings",
            subtitle = "Today layout: what shows in a session and outside one",
            icon = Icons.Outlined.Tune,
            onClick = {
                onDismiss()
                onArrange()
            },
            trailing = { AbSheetValue(null) },
        )
        // TD1: the More today bar (the chips at the foot of Today) can be hidden; nothing else moves.
        val context = LocalContext.current
        val shown by UiPrefs.moreTodayBar.collectAsState()
        AbSheetRow(
            title = "More today bar",
            subtitle = "The chips for the parts you put in More today",
            icon = Icons.Outlined.ViewStream,
            onClick = null,
            trailing = {
                AbSegmentedCompact(
                    options = listOf("Show", "Hide"), selectedIndex = if (shown) 0 else 1,
                    onSelect = { UiPrefs.setMoreTodayBar(context, it == 0) },
                    modifier = Modifier.semantics { contentDescription = "More today bar" },
                )
            },
        )
        if (noiseTitle != null) {
            AbSheetRow(
                title = "Noise",
                subtitle = noiseTitle,
                icon = Icons.Outlined.MusicNote,
                onClick = {
                    onDismiss()
                    onNoise()
                },
                trailing = { AbSheetValue(null) },
            )
        }
    }
}
