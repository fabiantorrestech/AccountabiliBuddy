package com.accountabilibuddy.companion.ui.today

import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.MusicNote
import androidx.compose.material.icons.outlined.Tune
import androidx.compose.runtime.Composable
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
            title = "Today layout",
            subtitle = "What shows in a session and outside one",
            icon = Icons.Outlined.Tune,
            onClick = {
                onDismiss()
                onArrange()
            },
            trailing = { AbSheetValue(null) },
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
