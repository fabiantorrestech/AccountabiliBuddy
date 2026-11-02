package com.accountabilibuddy.companion.ui.kit

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Lock
import androidx.compose.material3.Icon
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.text.AnnotatedString
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.ui.theme.Ab

/**
 * An info banner: the theme's info tones with a lock (or another icon) in front, for "Guards rest while you focus, back at
 * 2:20 PM." Pass an [AnnotatedString] to make part of it bold.
 */
@Composable
fun AbInfoBanner(text: AnnotatedString, modifier: Modifier = Modifier, icon: ImageVector = Icons.Outlined.Lock) {
    Surface(modifier.fillMaxWidth(), shape = Ab.shapes.mediumShape, color = Ab.colors.infoBg, contentColor = Ab.colors.infoInk) {
        Row(
            Modifier.padding(horizontal = 16.dp, vertical = 14.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            Icon(icon, contentDescription = null, modifier = Modifier.size(20.dp))
            Text(text, style = Ab.type.bodyMedium, modifier = Modifier.weight(1f))
        }
    }
}

/** An info banner with plain text. */
@Composable
fun AbInfoBanner(text: String, modifier: Modifier = Modifier, icon: ImageVector = Icons.Outlined.Lock) =
    AbInfoBanner(AnnotatedString(text), modifier, icon)
