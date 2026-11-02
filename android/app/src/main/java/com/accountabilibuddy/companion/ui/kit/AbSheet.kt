package com.accountabilibuddy.companion.ui.kit

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.rememberModalBottomSheetState
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.ui.theme.Ab

/** A bottom sheet (Material 3's modal sheet) in the theme's surface, with its radius on the top corners. */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun AbSheet(onDismiss: () -> Unit, modifier: Modifier = Modifier, skipPartiallyExpanded: Boolean = true, content: @Composable ColumnScope.() -> Unit) {
    val shapes = Ab.shapes
    val colors = Ab.colors
    ModalBottomSheet(
        onDismissRequest = onDismiss,
        sheetState = rememberModalBottomSheetState(skipPartiallyExpanded = skipPartiallyExpanded),
        shape = RoundedCornerShape(topStart = shapes.large, topEnd = shapes.large),
        containerColor = colors.surface,
        contentColor = colors.ink,
        scrimColor = androidx.compose.ui.graphics.Color.Black.copy(alpha = 0.5f),
        dragHandle = {
            Box(Modifier.padding(vertical = 12.dp).size(width = 36.dp, height = 4.dp).background(colors.line, Ab.shapes.pill))
        },
    ) {
        Column(
            modifier.fillMaxWidth().navigationBarsPadding().padding(horizontal = 20.dp).padding(bottom = 20.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp),
            horizontalAlignment = Alignment.Start,
            content = content,
        )
    }
}
