package com.accountabilibuddy.companion.ui.sheets

import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.compositionLocalOf
import com.accountabilibuddy.companion.ui.kit.AbDialog
import com.accountabilibuddy.companion.ui.theme.Ab

/**
 * A sheet flow that holds unsaved changes (FX-A, device finding 1: a swipe down closed the whole New goal flow and lost the draft).
 * The owner of the flow provides one with [ProvideSheetDiscardGuard]; every [AbSheet] drawn inside it then refuses a swipe down, a tap
 * outside and Back while [dirty] is true and calls [ask] instead, which shows [DiscardChangesDialog]. A sheet inside the flow that
 * has its own, smaller job (a new area opened over the goal) provides null around itself.
 */
class SheetDiscardGuard(val dirty: Boolean, val ask: () -> Unit)

/** The guard of the flow the sheet is in; null for a sheet that holds nothing to lose. */
val LocalSheetDiscardGuard = compositionLocalOf<SheetDiscardGuard?> { null }

/** Draws [content] under [guard] (null: no guard). */
@Composable
fun ProvideSheetDiscardGuard(guard: SheetDiscardGuard?, content: @Composable () -> Unit) {
    CompositionLocalProvider(LocalSheetDiscardGuard provides guard, content = content)
}

/** "Discard this goal?": Keep editing goes back to the sheet, Discard closes the flow and loses what was entered. */
@Composable
fun DiscardChangesDialog(title: String, onKeep: () -> Unit, onDiscard: () -> Unit) {
    AbDialog(title = title, onDismiss = onKeep, confirmText = "Discard", onConfirm = onDiscard, dismissText = "Keep editing") {
        Text("What you entered isn't saved yet.", style = Ab.type.body, color = Ab.colors.muted)
    }
}
