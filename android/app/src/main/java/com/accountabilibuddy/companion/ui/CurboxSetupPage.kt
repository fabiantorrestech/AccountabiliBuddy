package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.blocks.BlockGuard
import com.accountabilibuddy.companion.curbox.CurboxClient
import com.accountabilibuddy.companion.curbox.CurboxFocusGroup
import com.accountabilibuddy.companion.curbox.CurboxHardMode
import com.accountabilibuddy.companion.model.BlockRef
import com.accountabilibuddy.companion.ui.kit.AbCard
import com.accountabilibuddy.companion.ui.kit.AbSectionLabel
import com.accountabilibuddy.companion.ui.kit.AbTextButton
import com.accountabilibuddy.companion.ui.theme.Ab
import com.accountabilibuddy.shared.sync.SyncBlocker

/**
 * Setting up Curbox with AccountabiliBuddy (Settings › Guards › Phone guard › Advanced, first-run setup and the migration assistant):
 * what to make in Curbox, a live checklist from Curbox's API (Check again), allowing AccountabiliBuddy, when turned-off blocks
 * come back on, and hard mode (a focus group with exit off started for each session on another device). A guard section: while a
 * focus session runs its switches and buttons that change something take no tap.
 */
@Composable
internal fun CurboxSetupPage(onConnectCurbox: () -> Unit, onReenableTime: (Boolean, java.time.LocalTime) -> String?) =
    GuardsScope { CurboxSetupBody(onConnectCurbox, onReenableTime) }

@Composable
private fun CurboxSetupBody(onConnectCurbox: () -> Unit, onReenableTime: (Boolean, java.time.LocalTime) -> String?) {
    val context = LocalContext.current
    val client = remember { CurboxClient(context) }
    var check by remember { mutableIntStateOf(0) }
    var installed by remember { mutableStateOf(false) }
    var granted by remember { mutableStateOf(false) }
    var blockers by remember { mutableStateOf<List<SyncBlocker>>(emptyList()) }
    var groups by remember { mutableStateOf<List<CurboxFocusGroup>>(emptyList()) }
    LaunchedEffect(check) {
        installed = client.findPackage() != null
        val status = client.read()
        granted = status.granted
        blockers = client.blockers().orEmpty()
        groups = client.focusGroups().orEmpty()
    }

    Column(verticalArrangement = Arrangement.spacedBy(12.dp)) {
        AbCard {
            Text("Curbox", style = Ab.type.section, color = Ab.colors.ink)
            GuardsRestNote()
            Note(
                "Curbox blocks apps, keywords and reels on this phone. AccountabiliBuddy turns blocks you switched off back on, " +
                    "and can start a Curbox focus you can't exit during sessions on your other devices.",
            )
            AbSectionLabel("In Curbox")
            Note(
                "1. Turn on Curbox's accessibility services.\n" +
                    "2. Make an app blocker group with your distracting apps.\n" +
                    "3. Turn on the reel blocker (and a keyword group if you like).\n" +
                    "4. For hard mode: make a focus group with exit turned off.",
            )
        }
        AbCard(verticalSpacing = 4.dp) {
            CheckLine("Curbox installed", installed)
            CheckLine("AccountabiliBuddy allowed in Curbox", granted)
            CheckLine("An app blocker group", blockers.any { it.kind == BlockRef.CURBOX_APP_GROUP })
            CheckLine("Reel blocker on", blockers.any { it.kind == BlockRef.CURBOX_REEL_BLOCKER && it.on })
            CheckLine("A keyword group (optional)", blockers.any { it.kind == BlockRef.CURBOX_KEYWORD_GROUP })
            CheckLine("A focus group with exit off (for hard mode)", groups.any { !it.exitable })
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalAlignment = Alignment.CenterVertically) {
                if (installed && !granted) GuardTonalButton("Allow AccountabiliBuddy", onClick = onConnectCurbox)
                AbTextButton("Check again", onClick = { check++ })
            }
        }

        if (granted) {
            AbCard {
                var rearm by remember { mutableStateOf(BlockGuard.enabled(context)) }
                ToggleRow("Turn blocks back on", "A block you switch off comes back on at the day's re-enable time, with a heads-up 5 minutes before.", rearm) {
                    rearm = it
                    BlockGuard.setEnabled(context, it)
                }
                if (rearm) ReenableTimes(onReenableTime)
            }

            AbCard {
                var hard by remember { mutableStateOf(CurboxHardMode.enabled(context)) }
                var group by remember { mutableStateOf(CurboxHardMode.group(context)) }
                ToggleRow(
                    "Hard mode",
                    "For each session on another device, start this Curbox focus group for the session's time left (ends early with the session or your emergency time).",
                    hard,
                    enabled = groups.isNotEmpty(),
                ) {
                    hard = it
                    CurboxHardMode.setEnabled(context, it)
                }
                if (hard) {
                    var open by remember { mutableStateOf(false) }
                    val chosen = groups.firstOrNull { it.id == group }
                    Column {
                        GuardValueButton(chosen?.let { label(it) } ?: "Choose a focus group", onClick = { open = true })
                        DropdownMenu(expanded = open, onDismissRequest = { open = false }) {
                            groups.forEach { g ->
                                DropdownMenuItem(text = { Text(label(g)) }, onClick = {
                                    group = g.id
                                    CurboxHardMode.setGroup(context, g.id)
                                    open = false
                                })
                            }
                        }
                    }
                }
            }
        }
    }
}

private fun label(group: CurboxFocusGroup) = if (group.exitable) "${group.name} (can be exited)" else group.name

@Composable
private fun CheckLine(text: String, ok: Boolean) {
    Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.fillMaxWidth()) {
        Text(if (ok) "✓" else "○", color = if (ok) Ab.colors.met else Ab.colors.muted, fontWeight = FontWeight.Bold)
        Text("  $text", style = Ab.type.bodyMedium, color = Ab.colors.ink)
    }
}
