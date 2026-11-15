package com.accountabilibuddy.companion.ui

import android.content.Context
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.essentials.EssentialsKind
import com.accountabilibuddy.companion.essentials.EssentialsStore
import com.accountabilibuddy.companion.essentials.StarterApp
import com.accountabilibuddy.companion.essentials.StarterApps
import com.accountabilibuddy.companion.ui.kit.AbCheckRow
import com.accountabilibuddy.companion.ui.kit.AbSectionLabel

/** Setup, page "How your phone locks" (ADR 73): LockMyPhone-Extended or the built-in screen, with the download offer. */
@Composable
internal fun LockChoiceSetupPage() {
    Column(verticalArrangement = Arrangement.spacedBy(12.dp)) {
        Text("How your phone locks", style = MaterialTheme.typography.headlineSmall)
        Text(
            "During a session, at night and in the morning the phone can keep you to a few essential apps. LockMyPhone-Extended " +
                "really locks the phone; the built-in screen is softer. You can change this later in Settings › Guards › Essentials.",
            style = MaterialTheme.typography.bodyMedium,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
        GuardsScope { LockChoiceCard() }
    }
}

/**
 * Setup, page "When it engages" (ADR 73, 75): during focus sessions, at night and in the morning, each one switched on
 * or skipped. A switched-on list unfolds its starter apps, this phone's own defaults ticked. Starters only ever add to a
 * list (an existing install keeps everything it has); unticking takes away only what this page added. In ProductaBuddy
 * the night is off unless chosen here.
 */
@Composable
internal fun EssentialsOccasionsSetupPage() {
    val context = LocalContext.current
    val app = context.applicationContext as CompanionApp
    val inProductaBuddy = app.guidance.isCompanion
    Column(verticalArrangement = Arrangement.spacedBy(12.dp)) {
        Text("When it engages", style = MaterialTheme.typography.headlineSmall)
        Text(
            "Pick when the phone keeps you to your essentials, and start each list with this phone's own apps. Skip any of them; " +
                "the full lists are in Settings › Guards › Essentials and Settings › Sleep.",
            style = MaterialTheme.typography.bodyMedium,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
        GuardsScope {
            Column(verticalArrangement = Arrangement.spacedBy(16.dp)) {
                OccasionWithStarters(
                    title = "During focus sessions",
                    detail = "While a session runs on your PC (or the phone's test session)",
                    store = app.essentials,
                    starters = remember { StarterApps.focus(context) },
                    isOn = { app.essentials.onUnlockInSession },
                    setOn = { app.essentials.onUnlockInSession = it },
                )
                OccasionWithStarters(
                    title = "At night",
                    detail = if (inProductaBuddy) "From bedtime until your wake time. Off by default in ProductaBuddy." else "From bedtime until your wake time",
                    store = app.bedtimeEssentials,
                    starters = remember { StarterApps.night(context) },
                    isOn = { app.bedtimeEssentials.onUnlockInSession && (!inProductaBuddy || app.bedtimeEssentials.nightInProductaBuddy) },
                    setOn = { on ->
                        app.bedtimeEssentials.onUnlockInSession = on
                        if (inProductaBuddy) app.bedtimeEssentials.nightInProductaBuddy = on
                    },
                )
                MorningLockCard()
            }
        }
    }
}

/** Both pages in one, for the migration assistant ("What's new"). */
@Composable
internal fun EssentialsLockSetupPage() {
    Column(verticalArrangement = Arrangement.spacedBy(24.dp)) {
        LockChoiceSetupPage()
        EssentialsOccasionsSetupPage()
    }
}

/** One occasion's switch and, when on, its starter apps (ticking adds to [store]; unticking removes only what was added here). */
@Composable
private fun OccasionWithStarters(
    title: String,
    detail: String,
    store: EssentialsStore,
    starters: List<StarterApp>,
    isOn: () -> Boolean,
    setOn: (Boolean) -> Unit,
) {
    val context = LocalContext.current
    // During a session the lists can't grow (more allowed apps is a looser change).
    val locked = LocalGuardsLocked.current
    var on by remember { mutableStateOf(isOn()) }
    // What the list held when this page opened: never taken away here (C5).
    val before = remember { store.essentials.value.apps.map { it.packageName }.toSet() }
    var ticked by remember { mutableStateOf(starters.filter { it.ticked || it.packageName in before }.map { it.packageName }.toSet()) }
    // Switched on: the pre-ticked starters join the list straight away.
    LaunchedEffect(on) {
        if (on && !locked) ticked.filter { it !in before }.forEach { store.setAllowed(it, true) }
    }

    Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
        ToggleRow(title, detail, on, enabled = !locked) {
            setOn(it)
            on = it
        }
        if (on) {
            AbSectionLabel(if (store.kind == EssentialsKind.Bedtime) "Starter apps for the night" else "Starter apps")
            if (starters.isEmpty()) Note("No default apps found; add them in Settings.")
            starters.forEach { starter ->
                val checked = starter.packageName in ticked
                AbCheckRow(
                    text = "${starter.role} · ${appLabel(context, starter.packageName)}",
                    checked = checked,
                    onCheckedChange = { now ->
                        ticked = if (now) ticked + starter.packageName else ticked - starter.packageName
                        store.setAllowed(starter.packageName, now)
                    },
                    enabled = starter.packageName !in before && !locked,
                )
            }
        }
    }
}

private fun appLabel(context: Context, packageName: String): String = runCatching {
    val pm = context.packageManager
    pm.getApplicationLabel(pm.getApplicationInfo(packageName, 0)).toString()
}.getOrDefault(packageName)
