package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.Image
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.accountabilibuddy.companion.R

/** The app's icon, name and version, with the camp quote in the middle (like the PC's About tab). */
@Composable
fun AboutScreen(pcName: String?) {
    val context = LocalContext.current
    val version = remember {
        runCatching { context.packageManager.getPackageInfo(context.packageName, 0).versionName }.getOrNull() ?: "?"
    }

    Column(
        Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(horizontal = 24.dp, vertical = 32.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.spacedBy(8.dp),
    ) {
        Image(
            painter = painterResource(R.mipmap.ic_launcher),
            contentDescription = null,
            modifier = Modifier.size(88.dp).clip(RoundedCornerShape(20.dp)),
        )
        Text(stringResource(R.string.app_name), style = MaterialTheme.typography.headlineSmall, fontWeight = FontWeight.SemiBold)
        Text("Version $version", style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
        Spacer(Modifier.height(32.dp))
        Text(
            "\"Now just to make sure you don't slip up while in camp we assign every camper an accountability buddy...\"",
            style = MaterialTheme.typography.titleMedium.copy(fontStyle = FontStyle.Italic, lineHeight = 28.sp),
            textAlign = TextAlign.Center,
        )
        Spacer(Modifier.height(32.dp))
        Text(
            if (pcName == null) "Not linked to a PC. Link one from Settings." else "Linked to $pcName. Updates for the PC app are in its About tab.",
            style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
            textAlign = TextAlign.Center,
        )
    }
}
