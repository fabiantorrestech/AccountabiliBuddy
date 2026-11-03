package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.Image
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
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
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.R
import com.accountabilibuddy.companion.ui.kit.AbCard
import com.accountabilibuddy.companion.ui.theme.Ab

/** The app's icon, name and version, with the camp quote in the middle (like the PC's About tab). */
@Composable
fun AboutScreen(pcName: String?, onOpenWhatsNew: () -> Unit = {}) {
    val context = LocalContext.current
    // 5 taps on the logo within 3 seconds open the migration assistant.
    val taps = remember { mutableListOf<Long>() }
    val version = remember {
        runCatching { context.packageManager.getPackageInfo(context.packageName, 0).versionName }.getOrNull() ?: "?"
    }

    Column(
        Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(horizontal = 16.dp, vertical = 16.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        Image(
            painter = painterResource(R.mipmap.ic_launcher),
            contentDescription = null,
            modifier = Modifier.size(88.dp).clip(Ab.shapes.cardShape).clickable(
                interactionSource = remember { androidx.compose.foundation.interaction.MutableInteractionSource() },
                indication = null,
            ) {
                val now = System.currentTimeMillis()
                taps.removeAll { now - it > 3_000 }
                taps += now
                if (taps.size >= 5) {
                    taps.clear()
                    onOpenWhatsNew()
                }
            },
        )
        Column(horizontalAlignment = Alignment.CenterHorizontally) {
            Text(stringResource(R.string.app_name), style = Ab.type.head, color = Ab.colors.ink)
            Text("Version $version", style = Ab.type.bodyMedium, color = Ab.colors.muted)
        }
        Text(
            "\"Now to make sure you don't slip up while in camp, we assign every camper an accountability buddy...\"",
            style = Ab.type.section.copy(fontStyle = FontStyle.Italic),
            color = Ab.colors.ink,
            textAlign = TextAlign.Center,
            modifier = Modifier.padding(vertical = 12.dp),
        )
        // What the app is for, in the user's words.
        AbCard {
            Note("AccountabiliBuddy is a companion you install on the devices you use every day (your phone, tablet, personal computer and work computer) to help you use your time on purpose. It notices when you drift or get derailed, and gives you a few other productivity tools to make sitting down and getting sh*t done as straightforward as possible.")
            Note("It works best alongside a blocker for each platform when you really want to shut distractions out (e.g. Curbox on Android, Cold Turkey on Windows).")
            Note("It's not perfect; no tool can be.")
            Note("It's hard to focus in a world of digital noise, with everything vying for your attention. We're fighting back, to help you reach your goals and reclaim your time and attention. (:")
        }
        Text(
            if (pcName == null) "No PC yet. Sign in to Google Drive (Settings › Devices & sync) to bring one in." else "Syncing with $pcName. Updates for the PC app are in its Settings › About.",
            style = Ab.type.small,
            color = Ab.colors.muted,
            textAlign = TextAlign.Center,
        )
        // Credits for what the look is made from (ADR 49); the font licence texts are in the app's assets/licenses.
        Text(
            "Fonts: IBM Plex Mono, Manrope and Newsreader, under the SIL Open Font License 1.1. City list © GeoNames (geonames.org), CC BY 4.0.",
            style = Ab.type.small,
            color = Ab.colors.muted,
            textAlign = TextAlign.Center,
        )
    }
}
