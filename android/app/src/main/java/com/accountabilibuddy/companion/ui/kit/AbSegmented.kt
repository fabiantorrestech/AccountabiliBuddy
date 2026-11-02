package com.accountabilibuddy.companion.ui.kit

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.ui.theme.Ab

/**
 * A segmented control: a pill track with the options sharing its width equally; the selected one is a raised surface with
 * ink text. Labels never wrap, so keep them short. [selectedIndex] may be null (nothing selected), which lets two rows
 * show one choice between them.
 */
@Composable
fun AbSegmented(options: List<String>, selectedIndex: Int?, onSelect: (Int) -> Unit, modifier: Modifier = Modifier) {
    val colors = Ab.colors
    Row(
        modifier.fillMaxWidth().height(48.dp).background(colors.surface2, Ab.shapes.pill).padding(4.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        options.forEachIndexed { index, label ->
            val selected = index == selectedIndex
            Surface(
                onClick = { onSelect(index) },
                modifier = Modifier.weight(1f).height(40.dp),
                shape = Ab.shapes.pill,
                color = if (selected) colors.surface else colors.surface2,
                contentColor = if (selected) colors.ink else colors.muted,
                border = if (selected) BorderStroke(1.dp, colors.line) else null,
            ) {
                Box(Modifier.fillMaxWidth(), contentAlignment = Alignment.Center) {
                    Text(
                        label, style = Ab.type.small, fontWeight = if (selected) FontWeight.Bold else FontWeight.Medium,
                        maxLines = 1, overflow = TextOverflow.Ellipsis, textAlign = TextAlign.Center, modifier = Modifier.padding(horizontal = 4.dp),
                    )
                }
            }
        }
    }
}
