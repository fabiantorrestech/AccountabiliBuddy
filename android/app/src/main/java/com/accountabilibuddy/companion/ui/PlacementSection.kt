package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.PhonelinkLock
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.notify.PinnedTimelineService
import com.accountabilibuddy.companion.placement.PocketPenalty
import com.accountabilibuddy.companion.ui.kit.AbCard
import com.accountabilibuddy.companion.ui.kit.AbListRow
import com.accountabilibuddy.companion.ui.kit.ChangeWaitsSheet
import com.accountabilibuddy.companion.ui.theme.Ab
import kotlinx.coroutines.delay
import java.time.Instant
import java.time.ZoneId
import java.time.format.DateTimeFormatter
import com.accountabilibuddy.companion.usage.PhoneUsageReader

/** What phone placement needs and does, for Settings and the migration assistant. */
@Composable
private fun PlacementText() = GuardsScope { PlacementTextBody() }

@Composable
private fun PlacementTextBody() {
    Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
        GuardsRestNote()
        PlacementIntro()
        PlacementSwitches()
        PhoneDownSwitches()
        PlacementNeeds()
    }
}

// What sensing the phone's place means, and what the Phone down screen is.
@Composable
private fun PlacementIntro() {
    Note(
        "During a focus session on your PC the phone tells it where it is (in a bag, face down, face up, in a pocket or in use), " +
            "from its own sensors, so the PC can ask you to put it away. Only that label, times and counts leave the phone, never sensor " +
            "values. Nothing vibrates or sounds. If the phone is out for 3 minutes, the essentials screen opens. During a real session " +
            "with the guard on, unlocking shows a \"Phone down\" screen instead (Putting it away, Need it for work, Essentials / quick " +
            "reply, Emergency); it never covers a call.",
    )
}

/** The guard's own switch (sensing the phone's place). */
@Composable
private fun PlacementSwitches() {
    GuardSwitch(LocalContext.current.applicationContext as CompanionApp)
}

/** The two pocket penalties of the Phone down screen. */
@Composable
private fun PhoneDownSwitches() {
    val app = LocalContext.current.applicationContext as CompanionApp
    PenaltySwitch(
        app, PocketPenalty.UnlockFriction, "Unlock friction",
        "Just out of a pocket: the Phone down screen's options appear after 10 seconds.",
    )
    PenaltySwitch(
        app, PocketPenalty.NoPhoneFreeCredit, "No phone-free credit for pocket time",
        "More than about 5 minutes in a pocket and the session isn't phone-free (it breaks the streak); the PC's session review shows the pocket minutes.",
    )
}

// What placement needs and doesn't have yet: the timeline notification, Usage access.
@Composable
private fun PlacementNeeds() {
    val context = LocalContext.current
    val app = context.applicationContext as CompanionApp
    val timelineOn = app.store.state.collectAsState().value.timelineNotificationOn
    if (!timelineOn) {
        Text("Turn on the timeline notification for phone placement.", style = Ab.type.bodyMedium, color = Ab.colors.waiting)
    }
    if (!PhoneUsageReader(context).hasAccess()) {
        Note("Without Usage access the phone can't tell an essential app from another one, so any unlocked use counts as in use.")
    }
}

/** Settings → Guards → Phone guard → "Sense where the phone is": the guard's switch, how it works and what it needs. */
@Composable
internal fun PlacementSection() = GuardsScope {
    AbCard(contentPadding = PaddingValues(horizontal = 16.dp, vertical = 8.dp), verticalSpacing = 4.dp) {
        GuardsRestNote()
        PlacementSwitches()
        Hint(
            "During a focus session on your PC the phone tells it where it is (in a bag, face down, face up, in a pocket or in use), " +
                "from its own sensors, so the PC can ask you to put it away. Only that label, times and counts leave the phone, never sensor " +
                "values. Nothing vibrates or sounds. If the phone is out for 3 minutes, the essentials screen opens.",
            label = "What it senses",
        )
        PlacementNeeds()
    }
}

/**
 * Settings → Guards → Phone guard → "Phone down on unlock": what the Phone down screen offers (it has no switch of its own: it shows
 * during a real session with the placement guard on) and its two pocket penalties.
 */
@Composable
internal fun PhoneDownSection() = GuardsScope {
    AbCard(contentPadding = PaddingValues(horizontal = 16.dp, vertical = 12.dp), verticalSpacing = 4.dp) {
        AbListRow(
            "Phone down on unlock",
            icon = Icons.Outlined.PhonelinkLock,
            subtitle = "Put it away · need it for work · essentials · emergency",
            showChevron = false,
        )
        Hint(
            "During a real session with the placement guard on, unlocking shows a \"Phone down\" screen instead of the essentials screen: " +
                "Putting it away closes it; Need it for work, Essentials / quick reply and Emergency are the other ways on. It never covers a call.",
            label = "What the screen does",
        )
        PhoneDownSwitches()
    }
}

/** The migration assistant's page for the Phone down screen and its pocket penalties. */
@Composable
internal fun PhoneDownSetupPage() {
    Column(verticalArrangement = Arrangement.spacedBy(12.dp)) {
        Text("Phone down screen", style = Ab.type.head, color = Ab.colors.ink)
        Note(
            "During a real PC focus session with the placement guard on, unlocking the phone shows \"Phones down: in a bag or somewhere off " +
                "you\" instead of the essentials screen. Putting it away closes it; Need it for work, Essentials / quick reply and Emergency " +
                "are the other ways on. It never covers a call. Two pocket penalties are on by default; turning one off waits 10 minutes and " +
                "isn't possible during a session.",
        )
        PlacementText()
    }
}

/** The migration assistant's page for phone placement. */
@Composable
internal fun PlacementSetupPage() {
    Column(verticalArrangement = Arrangement.spacedBy(12.dp)) {
        Text("Phone placement", style = Ab.type.head, color = Ab.colors.ink)
        PlacementText()
    }
}

private val CLOCK: DateTimeFormatter = DateTimeFormatter.ofPattern("h:mm a")

/** The guard's on/off switch: on at once, off waits 10 minutes and is refused during a focus session. */
@Composable
private fun GuardSwitch(app: CompanionApp) {
    val guard = app.placementGuard
    var on by remember { mutableStateOf(guard.isOn(Instant.now())) }
    var offAt by remember { mutableStateOf(guard.pendingOffAt(Instant.now())) }
    var locked by remember { mutableStateOf(guard.locked(Instant.now())) }
    // A pending off comes due, or a session starts, while the page is open.
    LaunchedEffect(Unit) {
        while (true) {
            val now = Instant.now()
            val wasOn = on
            on = guard.isOn(now)
            offAt = guard.pendingOffAt(now)
            locked = guard.locked(now)
            if (wasOn && !on) PinnedTimelineService.checkPlacement()
            delay(2_000L)
        }
    }

    val detail = when {
        locked && on -> "Locked during a focus session"
        offAt != null -> "Turns off at ${CLOCK.format(offAt!!.atZone(ZoneId.systemDefault()))}"
        else -> "Reads the phone's sensors during PC focus sessions. Off: no sensors and the PC never nags about placement."
    }
    var asking by remember { mutableStateOf(false) }
    fun apply(wantOn: Boolean) {
        val now = Instant.now()
        if (wantOn) {
            guard.turnOn()
        } else {
            guard.requestOff(now)
        }

        on = guard.isOn(now)
        offAt = guard.pendingOffAt(now)
        PinnedTimelineService.checkPlacement()
    }

    ToggleRow("Sense where the phone is", detail, on && offAt == null, enabled = !(locked && on)) { wantOn -> if (wantOn) apply(true) else asking = true }
    // Turning it off is a looser change: it waits, then applies on its own (board 5h). "Queue it" starts that wait; nothing shortens it.
    if (asking) {
        ChangeWaitsSheet(
            title = "Turn off the phone placement guard?",
            body = "This waits ${guard.offWait.toMinutes()} minutes. If you still want it then, it turns off on its own.",
            appliesAt = Instant.now().plus(guard.offWait),
            about = true,
            onQueue = { apply(false) },
            onDismiss = { asking = false },
        )
    }
}

/**
 * One pocket penalty's switch (on by default): on applies at once; off waits 10 minutes and is refused during a focus
 * session, like the guard's switch.
 */
@Composable
private fun PenaltySwitch(app: CompanionApp, penalty: PocketPenalty, title: String, description: String) {
    val store = app.phoneDown
    var on by remember { mutableStateOf(store.isOn(penalty, Instant.now())) }
    var offAt by remember { mutableStateOf(store.pendingOffAt(penalty, Instant.now())) }
    var locked by remember { mutableStateOf(store.locked(Instant.now())) }
    // A pending off comes due, or a session starts, while the page is open.
    LaunchedEffect(Unit) {
        while (true) {
            val now = Instant.now()
            on = store.isOn(penalty, now)
            offAt = store.pendingOffAt(penalty, now)
            locked = store.locked(now)
            delay(2_000L)
        }
    }

    val detail = when {
        locked && on -> "Locked during a focus session"
        offAt != null -> "Turns off at ${CLOCK.format(offAt!!.atZone(ZoneId.systemDefault()))}"
        else -> description
    }
    var asking by remember { mutableStateOf(false) }
    fun apply(wantOn: Boolean) {
        val now = Instant.now()
        if (wantOn) {
            store.turnOn(penalty)
        } else {
            store.requestOff(penalty, now)
        }

        on = store.isOn(penalty, now)
        offAt = store.pendingOffAt(penalty, now)
    }

    ToggleRow(title, detail, on && offAt == null, enabled = !(locked && on)) { wantOn -> if (wantOn) apply(true) else asking = true }
    // Turning it off is a looser change: it waits, then applies on its own (board 5h). "Queue it" starts that wait; nothing shortens it.
    if (asking) {
        ChangeWaitsSheet(
            title = "Turn off \"$title\"?",
            body = "This waits ${store.offWait.toMinutes()} minutes. If you still want it then, it turns off on its own.",
            appliesAt = Instant.now().plus(store.offWait),
            about = true,
            onQueue = { apply(false) },
            onDismiss = { asking = false },
        )
    }
}
