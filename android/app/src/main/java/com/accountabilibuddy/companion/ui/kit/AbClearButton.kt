package com.accountabilibuddy.companion.ui.kit

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Close
import androidx.compose.material3.Icon
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.ui.theme.Ab

/**
 * The little "x" at the end of a search box: a 48 dp touch target named "Clear search". Show it only while the box has text;
 * [onClick] should set the text to "" (the box keeps its focus, since a tap on this does not take it).
 */
@Composable
fun AbClearButton(onClick: () -> Unit, modifier: Modifier = Modifier, tint: Color = Ab.colors.muted) {
    Box(
        modifier.size(48.dp).clip(CircleShape).clickable(role = Role.Button, onClick = onClick),
        contentAlignment = Alignment.Center,
    ) {
        Icon(Icons.Outlined.Close, contentDescription = "Clear search", tint = tint, modifier = Modifier.size(20.dp))
    }
}
