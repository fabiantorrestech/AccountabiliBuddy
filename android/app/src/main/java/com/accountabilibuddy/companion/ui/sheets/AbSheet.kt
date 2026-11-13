package com.accountabilibuddy.companion.ui.sheets

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.outlined.ArrowBack
import androidx.compose.material3.BottomSheetDefaults
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.Text
import androidx.compose.material3.rememberModalBottomSheetState
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalConfiguration
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.accountabilibuddy.companion.ui.theme.Ab

/**
 * The round 2 phone sheet (brief §3.7; R7 reuses it): one of two fixed heights ([AbSheetHeight]), a drag handle, the [title] with a
 * muted [subtitle], an optional accent back arrow ([onBack], for a nested page), an optional [titleAction] at the end of the title
 * line ("Goals ›"), the content, and a sticky [footer] for a primary action or a note. The header and the footer stay put; content
 * longer than the sheet scrolls between them. The scrim is the theme's scrim at 60%. It always opens at its full height (no
 * half-open step).
 *
 * Rows are [AbSheetRow]s (58 dp or more). The older free-height sheet is [com.accountabilibuddy.companion.ui.kit.AbSheet]; this one is
 * for the round 2 menus and pickers.
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun AbSheet(
    title: String,
    onDismiss: () -> Unit,
    height: AbSheetHeight = AbSheetHeight.Half,
    subtitle: String? = null,
    onBack: (() -> Unit)? = null,
    titleAction: (@Composable () -> Unit)? = null,
    footer: (@Composable ColumnScope.() -> Unit)? = null,
    aboveKeyboard: Boolean = false,
    content: @Composable ColumnScope.() -> Unit,
) {
    val colors = Ab.colors
    val shapes = Ab.shapes
    val sheetHeight = (LocalConfiguration.current.screenHeightDp * height.fraction).dp
    ModalBottomSheet(
        onDismissRequest = onDismiss,
        sheetState = rememberModalBottomSheetState(skipPartiallyExpanded = true),
        // A sheet that is typed in handles the keyboard itself (below), so the sheet never grows or moves when the keyboard comes up.
        contentWindowInsets = if (aboveKeyboard) ({ WindowInsets(0, 0, 0, 0) }) else ({ BottomSheetDefaults.windowInsets }),
        shape = RoundedCornerShape(topStart = shapes.large, topEnd = shapes.large),
        containerColor = colors.surface,
        contentColor = colors.ink,
        scrimColor = MaterialTheme.colorScheme.scrim.copy(alpha = 0.6f),
        dragHandle = {
            Box(Modifier.padding(top = 10.dp, bottom = 6.dp).size(width = 36.dp, height = 4.dp).background(colors.faint, shapes.pill))
        },
    ) {
        // A sheet that is typed in ([aboveKeyboard]) keeps its height and place: its content and footer make room for the keyboard
        // inside it, following the keyboard as it slides in, so the sheet itself never jumps.
        Column(Modifier.fillMaxWidth().height(sheetHeight).navigationBarsPadding().then(if (aboveKeyboard) Modifier.imePadding() else Modifier)) {
            Row(
                Modifier.fillMaxWidth().padding(start = if (onBack != null) 4.dp else 24.dp, end = 12.dp, top = 4.dp, bottom = 8.dp),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                if (onBack != null) {
                    IconButton(onClick = onBack) {
                        Icon(Icons.AutoMirrored.Outlined.ArrowBack, contentDescription = "Back", tint = colors.accent, modifier = Modifier.size(22.dp))
                    }
                }
                Column(Modifier.weight(1f)) {
                    Text(
                        title, style = Ab.type.body.copy(fontSize = 17.sp), fontWeight = FontWeight.SemiBold, color = colors.ink,
                        maxLines = 1, overflow = TextOverflow.Ellipsis, modifier = Modifier.semantics { heading() },
                    )
                    if (subtitle != null) {
                        Text(subtitle, style = Ab.type.small.copy(fontSize = 13.5.sp), color = colors.muted, maxLines = 2, overflow = TextOverflow.Ellipsis)
                    }
                }
                titleAction?.invoke()
            }
            Column(
                Modifier.weight(1f).fillMaxWidth().verticalScroll(rememberScrollState()).padding(bottom = 8.dp),
                verticalArrangement = Arrangement.Top,
                content = content,
            )
            if (footer != null) {
                HorizontalDivider(color = colors.line, thickness = 1.dp)
                Column(Modifier.fillMaxWidth().padding(horizontal = 24.dp, vertical = 12.dp), verticalArrangement = Arrangement.spacedBy(8.dp), content = footer)
            }
        }
    }
}
