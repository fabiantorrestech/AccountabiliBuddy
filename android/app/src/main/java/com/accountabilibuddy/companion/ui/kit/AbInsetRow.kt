package com.accountabilibuddy.companion.ui.kit

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.RowScope
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Surface
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.ui.theme.Ab

/** A row inset into a card on the second tone (a task, a step, a device). With [onClick] it is a button. */
@Composable
fun AbInsetRow(
    modifier: Modifier = Modifier,
    onClick: (() -> Unit)? = null,
    contentPadding: PaddingValues = PaddingValues(horizontal = 14.dp, vertical = 10.dp),
    content: @Composable RowScope.() -> Unit,
) {
    val body: @Composable () -> Unit = {
        Row(
            Modifier.fillMaxWidth().heightIn(min = 48.dp).padding(contentPadding),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(12.dp),
            content = content,
        )
    }
    if (onClick == null) {
        Surface(modifier.fillMaxWidth(), shape = Ab.shapes.mediumShape, color = Ab.colors.surface2, contentColor = Ab.colors.ink, content = body)
    } else {
        Surface(onClick = onClick, modifier = modifier.fillMaxWidth(), shape = Ab.shapes.mediumShape, color = Ab.colors.surface2, contentColor = Ab.colors.ink, content = body)
    }
}
