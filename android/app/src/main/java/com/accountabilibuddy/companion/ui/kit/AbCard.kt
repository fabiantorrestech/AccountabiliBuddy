package com.accountabilibuddy.companion.ui.kit

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Surface
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.ui.theme.Ab

/**
 * A card: the theme's surface, corner radius and edge (a hairline on Instrument and Paper, tone only on Night Desk). Fills
 * the width it is given; the height follows its content. With [onClick] the whole card is a button.
 */
@Composable
fun AbCard(
    modifier: Modifier = Modifier,
    onClick: (() -> Unit)? = null,
    contentPadding: PaddingValues = PaddingValues(18.dp),
    verticalSpacing: Dp = 10.dp,
    content: @Composable ColumnScope.() -> Unit,
) {
    val shapes = Ab.shapes
    val colors = Ab.colors
    val border = if (shapes.cardEdge > 0.dp) BorderStroke(shapes.cardEdge, colors.line) else null
    val body: @Composable () -> Unit = {
        Column(Modifier.fillMaxWidth().padding(contentPadding), verticalArrangement = Arrangement.spacedBy(verticalSpacing), content = content)
    }
    if (onClick == null) {
        Surface(modifier.fillMaxWidth(), shape = shapes.cardShape, color = colors.surface, contentColor = colors.ink, border = border, content = body)
    } else {
        Surface(onClick = onClick, modifier = modifier.fillMaxWidth(), shape = shapes.cardShape, color = colors.surface, contentColor = colors.ink, border = border, content = body)
    }
}
