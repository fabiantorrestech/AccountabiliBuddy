package com.accountabilibuddy.companion.ui.goals

import android.content.ActivityNotFoundException
import android.content.Context
import android.content.Intent
import android.net.Uri
import android.provider.Settings
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.items
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Add
import androidx.compose.material.icons.outlined.Check
import androidx.compose.material.icons.outlined.Close
import androidx.compose.material.icons.outlined.Computer
import androidx.compose.material.icons.outlined.PhoneAndroid
import androidx.compose.material.icons.outlined.Shield
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.produceState
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.listSaver
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.saveable.Saver
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.input.KeyboardCapitalization
import androidx.compose.ui.unit.dp
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleEventObserver
import androidx.lifecycle.compose.LocalLifecycleOwner
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.essentials.AppCatalog
import com.accountabilibuddy.companion.goals.PhoneScreenTime
import com.accountabilibuddy.companion.ui.AbLazyColumn
import com.accountabilibuddy.companion.ui.kit.AbChip
import com.accountabilibuddy.companion.ui.kit.AbOutlinedButton
import com.accountabilibuddy.companion.ui.kit.AbPrimaryButton
import com.accountabilibuddy.companion.ui.kit.AbQuietIconButton
import com.accountabilibuddy.companion.ui.kit.AbSwitch
import com.accountabilibuddy.companion.ui.kit.AbTextButton
import com.accountabilibuddy.companion.ui.kit.AbTextField
import com.accountabilibuddy.companion.ui.kit.AbTonalButton
import com.accountabilibuddy.companion.ui.sheets.AbSheet
import com.accountabilibuddy.companion.ui.sheets.AbSheetHeight
import com.accountabilibuddy.companion.ui.sheets.AbSheetRow
import com.accountabilibuddy.companion.ui.theme.Ab
import com.accountabilibuddy.shared.goals.GoalAppMatchKind
import com.accountabilibuddy.shared.goals.GoalScreenTimeEditor
import com.accountabilibuddy.shared.goals.GoalSourceRule
import com.accountabilibuddy.shared.sync.SyncDeviceCard
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

/**
 * The screen-time source's page of "+ Source" (ST3; the PC's Tracking and sources twin): the apps and sites that count as a list (a chip
 * names each one's kind and a tap changes it; typing guesses the kind), a pick list of apps seen on this phone, and which devices count.
 * Usage access is the permission this phone reads screen time with: the row says whether it is on and opens the system's page.
 *
 * Privacy: the apps and sites the user lists are part of the goal: they are sealed at rest and travel with the goal, sealed, to the user's own
 * devices only. This phone's usage records and the pick list stay on the phone; only each device's minutes per day are counted from the other
 * devices. Nothing here is logged.
 *
 * @param rule The source being made or changed; a new one carries this phone's id in `createdOn` ([GoalScreenTimeEditor.newRule]).
 * @param onSave Takes the finished rule (Add source / Save).
 */
@Composable
fun ScreenTimeSourceSheet(rule: GoalSourceRule, creating: Boolean, onSave: (GoalSourceRule) -> Unit, onDismiss: () -> Unit, onBack: () -> Unit) {
    val context = LocalContext.current
    val app = context.applicationContext as CompanionApp
    val colors = Ab.colors
    // Kept across a rotation: the entries and devices as edited so far, laid over the rule the page was opened with.
    val saver = remember(rule.id) { ruleSaver(rule) }
    var edited by rememberSaveable(stateSaver = saver) { mutableStateOf(rule) }
    var typed by rememberSaveable { mutableStateOf("") }
    var chosenKind by remember { mutableStateOf<GoalAppMatchKind?>(null) }
    val self = app.sync.deviceId
    val received by app.sync.received.collectAsStateWithLifecycle()
    // Other devices count only while goals sync is on and this phone is in a group; otherwise they are greyed, as on the PC.
    val syncSettings by app.sync.settings.collectAsStateWithLifecycle()
    val groupKey by app.sync.key.collectAsStateWithLifecycle()
    val syncOn = syncSettings.shareTaskLists && groupKey != null
    val others = remember(received) { received.map { it.file.device }.filter { it.kind == SyncDeviceCard.KIND_PC || it.kind == SyncDeviceCard.KIND_PHONE }.distinctBy { it.deviceId } }
    val known = remember(others) { others.map { it.deviceId } }

    val access = rememberUsageAccess(context)
    // The pick list and the "seen" notes: launchable apps plus the last week's usage (names and packages, never leaving the phone).
    val seen by produceState(Seen.Empty, access) {
        value = withContext(Dispatchers.IO) {
            val catalog = runCatching { AppCatalog.apps(context) }.getOrDefault(emptyList()).map { it.packageName to it.name }
            val used = if (access) runCatching { PhoneScreenTime.seenApps(context) }.getOrDefault(emptyList()) else emptyList()
            Seen.of(used + catalog)
        }
    }

    val problem = GoalScreenTimeEditor.problem(edited)
    AbSheet(
        title = "Screen time",
        subtitle = "Apps and sites that count · minutes add up across the devices you pick",
        onDismiss = onDismiss, height = AbSheetHeight.Tall, onBack = onBack,
        footer = {
            problem?.let { Text(it, style = Ab.type.small, color = colors.muted) }
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                AbOutlinedButton("Back", onClick = onBack, modifier = Modifier.weight(1f))
                AbPrimaryButton(if (creating) "Add source" else "Save", onClick = { onSave(edited) }, enabled = problem == null, modifier = Modifier.weight(1f))
            }
        },
    ) {
        UsageAccessRow(access, context)

        Head("APPS AND SITES")
        val entries = edited.apps.orEmpty()
        if (entries.isEmpty()) {
            Text("None yet. Pick an app below, or type an app id, an app name or a site.", style = Ab.type.small, color = colors.muted, modifier = Modifier.padding(horizontal = 24.dp, vertical = 6.dp))
        }
        entries.forEachIndexed { index, entry ->
            val appName = if (entry.kind == GoalAppMatchKind.Package) seen.nameOf(entry.text) else null
            val note = GoalScreenTimeEditor.note(entry, seen.keys)
            AbSheetRow(
                appName ?: entry.text, onClick = null,
                subtitle = listOfNotNull(if (appName != null) entry.text else null, note).joinToString(" · ").ifEmpty { null },
                trailing = {
                    Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(2.dp)) {
                        AbChip(GoalScreenTimeEditor.kindLabel(entry.kind), selected = true, onClick = { edited = GoalScreenTimeEditor.cycleKind(edited, index) })
                        AbQuietIconButton(Icons.Outlined.Close, "Remove", onClick = { edited = GoalScreenTimeEditor.remove(edited, index) })
                    }
                },
            )
        }

        val guess = GoalScreenTimeEditor.guess(typed)
        AbTextField(
            typed, { typed = it.take(GoalScreenTimeEditor.MAX_TEXT); chosenKind = null }, "Add an app id, app name or site",
            Modifier.padding(horizontal = 24.dp, vertical = 6.dp), capitalization = KeyboardCapitalization.None,
        )
        if (guess != null) {
            val kind = chosenKind ?: guess.kind
            Row(Modifier.padding(horizontal = 24.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                Text("Counts as", style = Ab.type.small, color = colors.muted)
                AbChip(GoalScreenTimeEditor.kindLabel(kind), selected = true, onClick = { chosenKind = GoalScreenTimeEditor.nextKind(kind) })
                AbTonalButton("Add", icon = Icons.Outlined.Add, onClick = {
                    edited = GoalScreenTimeEditor.add(edited, typed, chosenKind)
                    typed = ""
                    chosenKind = null
                })
            }
        }

        Head("SEEN ON THIS PHONE")
        val shown = remember(seen, typed) { seen.apps.filter { GoalScreenTimeEditor.matchesTyped(typed, it.second, it.first) } }
        // Two apps with one name (Chrome and Chrome Beta) show their ids to tell them apart.
        val sameName = remember(shown) { shown.groupingBy { it.second.lowercase() }.eachCount() }
        if (shown.isEmpty()) {
            Text(
                if (seen.apps.isEmpty()) "Nothing to pick yet." else "No app matches what you typed.",
                style = Ab.type.small, color = colors.muted, modifier = Modifier.padding(horizontal = 24.dp, vertical = 6.dp),
            )
        } else {
            // A fixed height that scrolls on its own, so the page keeps its length however many apps the phone has.
            AbLazyColumn(Modifier.fillMaxWidth().height(240.dp)) {
                items(shown, key = { it.first }) { (pkg, name) ->
                    val added = entries.any { it.kind == GoalAppMatchKind.Package && it.text == pkg }
                    AbSheetRow(
                        name, onClick = { edited = GoalScreenTimeEditor.add(edited, pkg, GoalAppMatchKind.Package) }, enabled = !added,
                        subtitle = if (added) "Already counting" else if ((sameName[name.lowercase()] ?: 0) > 1) pkg else null,
                        trailing = { if (added) Icon(Icons.Outlined.Check, contentDescription = null, tint = colors.muted) },
                    )
                }
            }
        }

        Head("COUNTS FROM")
        val selected = GoalScreenTimeEditor.selected(edited, self, known)
        DeviceRow("This phone", Icons.Outlined.PhoneAndroid, lastOne = selected.size == 1 && self in selected, checked = self in selected, enabled = true, alone = others.isEmpty()) {
            edited = GoalScreenTimeEditor.withDevice(edited, self, it, self, known)
        }
        others.forEach { device ->
            val enabled = GoalScreenTimeEditor.OTHER_DEVICES_ENABLED && syncOn
            DeviceRow(
                device.name, if (device.kind == SyncDeviceCard.KIND_PC) Icons.Outlined.Computer else Icons.Outlined.PhoneAndroid,
                lastOne = selected.size == 1 && device.deviceId in selected, checked = device.deviceId in selected, enabled = enabled,
                note = if (enabled) null else if (!GoalScreenTimeEditor.OTHER_DEVICES_ENABLED) GoalScreenTimeEditor.NEEDS_GOALS_SYNC else "Needs Sync task lists on, in a group",
            ) { edited = GoalScreenTimeEditor.withDevice(edited, device.deviceId, it, self, known) }
        }
        Text(
            "Each device reads its own usage and sends only its minutes for the day. The apps and sites listed here travel with the goal, sealed, to your own devices only.",
            style = Ab.type.small, color = colors.muted, modifier = Modifier.padding(horizontal = 24.dp, vertical = 10.dp),
        )
    }
}

// What an unsaved edit holds: the entries (kind and text) and the device choice, laid back over [base] (its id and other fields stay).
private fun ruleSaver(base: GoalSourceRule): Saver<GoalSourceRule, Any> = listSaver(
    save = { rule ->
        listOf(
            rule.apps.orEmpty().map { it.kind.name + "|" + it.text },
            rule.devices?.map { it.toString() } ?: emptyList<String>(), rule.devices != null, rule.allDevices,
        )
    },
    restore = { saved ->
        @Suppress("UNCHECKED_CAST")
        val apps = (saved[0] as List<String>).mapNotNull { line ->
            val kind = runCatching { GoalAppMatchKind.valueOf(line.substringBefore('|')) }.getOrNull() ?: return@mapNotNull null
            val text = line.substringAfter('|')
            base.apps.orEmpty().firstOrNull { it.kind == kind && it.text == text } ?: com.accountabilibuddy.shared.goals.GoalAppMatch(kind, text)
        }
        @Suppress("UNCHECKED_CAST")
        val devices = if (saved[2] as Boolean) (saved[1] as List<String>).mapNotNull { runCatching { java.util.UUID.fromString(it) }.getOrNull() } else null
        base.copy(apps = apps, devices = devices, allDevices = saved[3] as Boolean)
    },
)

@Composable
private fun DeviceRow(name: String, icon: ImageVector, lastOne: Boolean, checked: Boolean, enabled: Boolean, note: String? = null, alone: Boolean = false, onChange: (Boolean) -> Unit) {
    // A greyed device that is still ticked can be unticked (never the last one).
    val canTap = (enabled || checked) && !(checked && lastOne)
    // FX-A (device finding 4): the last counting device can't be switched off, so it shows a check, not a switch whose greyed track reads as "off".
    val fixed = checked && lastOne
    AbSheetRow(
        name, onClick = if (canTap && (enabled || checked)) ({ onChange(!checked) }) else null, icon = icon, enabled = enabled || checked || fixed,
        subtitle = note ?: if (fixed) (if (alone) "Always counts while it's the only device" else "At least one device counts") else null,
        trailing = {
            if (fixed) Icon(Icons.Outlined.Check, contentDescription = "Counts", tint = Ab.colors.accent) else AbSwitch(checked, enabled = canTap, onCheckedChange = onChange)
        },
    )
}

/** The Usage access row: whether it is on, and a button to the system's page (no new permission; the same grant as Essentials and Stats). */
@Composable
internal fun UsageAccessRow(on: Boolean, context: Context) {
    AbSheetRow(
        "Usage access", onClick = null, icon = Icons.Outlined.Shield,
        subtitle = if (on) "On · this phone can read its screen time" else "Off · screen time can't be read until you turn it on",
        trailing = { if (!on) AbTextButton("Open settings", onClick = { openUsageAccess(context) }) },
    )
}

/** Whether Usage access is on; read again whenever the app comes back to the front (the user turns it on in Settings). */
@Composable
internal fun rememberUsageAccess(context: Context): Boolean {
    var tick by remember { mutableIntStateOf(0) }
    val owner = LocalLifecycleOwner.current
    DisposableEffect(owner) {
        val observer = LifecycleEventObserver { _, event -> if (event == Lifecycle.Event.ON_RESUME) tick++ }
        owner.lifecycle.addObserver(observer)
        onDispose { owner.lifecycle.removeObserver(observer) }
    }
    return remember(tick) { runCatching { PhoneScreenTime.hasAccess(context) }.getOrDefault(false) }
}

/** The system's Usage access page (the app's own row when the system has one). */
internal fun openUsageAccess(context: Context) {
    val intent = Intent(Settings.ACTION_USAGE_ACCESS_SETTINGS).setData(Uri.fromParts("package", context.packageName, null)).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
    try {
        context.startActivity(intent)
    } catch (e: ActivityNotFoundException) {
        context.startActivity(Intent(Settings.ACTION_USAGE_ACCESS_SETTINGS).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK))
    }
}

@Composable
private fun Head(text: String) {
    Text(text, style = Ab.type.micro, color = Ab.colors.muted, modifier = Modifier.fillMaxWidth().padding(start = 24.dp, top = 14.dp, bottom = 4.dp))
}

/** What this phone has: (package, shown name) pairs for the pick list, and the lower-cased packages and names for the "not seen" note. */
private class Seen(val apps: List<Pair<String, String>>) {
    private val names = apps.toMap()
    val keys: Set<String> = apps.flatMap { listOf(it.first, it.second.lowercase()) }.toSet()
    fun nameOf(pkg: String): String? = names[pkg]?.takeIf { it != pkg }

    companion object {
        val Empty = Seen(emptyList())
        fun of(pairs: List<Pair<String, String>>) = Seen(pairs.distinctBy { it.first }.sortedBy { it.second.lowercase() })
    }
}
