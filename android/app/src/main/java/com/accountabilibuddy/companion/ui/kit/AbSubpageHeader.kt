package com.accountabilibuddy.companion.ui.kit

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.outlined.ArrowBack
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.ui.theme.Ab

/** A page one level down: a "← Settings" crumb that goes back, then the page's title in the display font. */
@Composable
fun AbSubpageHeader(crumb: String, title: String, onBack: () -> Unit, modifier: Modifier = Modifier) {
    Column(modifier.fillMaxWidth().padding(horizontal = 4.dp), verticalArrangement = Arrangement.spacedBy(2.dp)) {
        Row(
            Modifier.heightIn(min = 44.dp).clickable(role = Role.Button, onClick = onBack),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(10.dp),
        ) {
            Icon(Icons.AutoMirrored.Outlined.ArrowBack, contentDescription = "Back to $crumb", tint = Ab.colors.muted, modifier = Modifier.size(20.dp))
            Text(crumb, style = Ab.type.bodyMedium, fontWeight = FontWeight.Medium, color = Ab.colors.muted)
        }
        Text(title, style = Ab.type.title, color = Ab.colors.ink)
    }
}
