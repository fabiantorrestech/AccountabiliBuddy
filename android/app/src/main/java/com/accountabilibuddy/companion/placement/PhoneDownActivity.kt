package com.accountabilibuddy.companion.placement

import android.app.KeyguardManager
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.os.PowerManager
import androidx.activity.ComponentActivity
import androidx.activity.compose.BackHandler
import androidx.activity.compose.setContent
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Call
import androidx.compose.material.icons.outlined.Lock
import androidx.compose.material.icons.outlined.Work
import androidx.compose.material.icons.outlined.Warning
import androidx.compose.material3.Button
import androidx.compose.material3.Icon
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.essentials.EssentialsActivity
import com.accountabilibuddy.companion.essentials.EssentialsLaunch
import com.accountabilibuddy.companion.nudge.EmergencyChooser
import com.accountabilibuddy.companion.nudge.Takeover
import com.accountabilibuddy.companion.ui.CompanionTheme
import com.accountabilibuddy.companion.usage.PhoneUsageReader
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.withContext
import java.time.Duration
import java.time.Instant
import java.time.ZoneId
import java.time.format.DateTimeFormatter
import java.util.Locale

/**
 * "Phones down: in a bag or somewhere off you." The screen an unlock lands on during a real PC focus session with the
 * placement guard on, instead of the essentials screen ([PhoneDownLaunch]). Shows the session's time left and the
 * pickups so far, and four ways on: Putting it away (closes it), Need it for work (the work use chooser, with its 4th-use
 * friction), Essentials / quick reply, and Emergency (the emergency chooser).
 *
 * Firm like the essentials screen: Back does nothing, and Home brings it back in about 3 seconds (once the app in front
 * isn't an essential). Closing itself is only for an option picked, the end of the conditions ([PhoneDownLaunch.applies]:
 * the session, a break, lunch, a call), or Putting it away. With the "Unlock friction" penalty on and the phone just out
 * of a pocket ([EXTRA_POCKET]), the options appear after [PENALTY_WAIT_SECONDS] seconds.
 */
class PhoneDownActivity : ComponentActivity() {
    private val handler = Handler(Looper.getMainLooper())
    // An option closed the screen on purpose (or it's going away): no coming back.
    private var done = false

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        val app = application as CompanionApp
        val pocket = intent.getBooleanExtra(EXTRA_POCKET, false)
        setContent {
            CompanionTheme {
                PhoneDownScreen(
                    app, pocket,
                    onPutAway = { done = true; finish() },
                    onEssentials = { toEssentials() },
                )
            }
        }
    }

    override fun onResume() {
        super.onResume()
        (application as CompanionApp).phoneDownOnScreen = true
        handler.removeCallbacksAndMessages(null)
        stillAppliesCheck.run()
    }

    override fun onStop() {
        super.onStop()
        handler.removeCallbacksAndMessages(null)
        (application as CompanionApp).phoneDownOnScreen = false
        if (!done && !isFinishing) {
            handler.postDelayed(reopenCheck, REOPEN_DELAY_MS)
        }
    }

    override fun onDestroy() {
        handler.removeCallbacksAndMessages(null)
        super.onDestroy()
    }

    // Closes when the conditions end while it's showing: a break, lunch, the session's end, a work use or emergency
    // started elsewhere, a call (the screen is never in the way of one).
    private val stillAppliesCheck: Runnable = object : Runnable {
        override fun run() {
            if (!PhoneDownLaunch.applies(this@PhoneDownActivity, Instant.now())) {
                done = true
                finish()
                return
            }

            handler.postDelayed(this, CHECK_MS)
        }
    }

    // Home (or anything that hid the screen): back after ~3 s, as long as the conditions hold, the phone is unlocked and
    // the app in front isn't an essential (with Usage access; without it nothing is known and it comes back at once).
    private val reopenCheck: Runnable = object : Runnable {
        override fun run() {
            val app = application as CompanionApp
            val now = Instant.now()
            val awake = getSystemService(PowerManager::class.java).isInteractive && !getSystemService(KeyguardManager::class.java).isKeyguardLocked
            if (done || !awake || !PhoneDownLaunch.applies(this@PhoneDownActivity, now) || app.phoneDownOnScreen || app.essentialsOnScreen) {
                return
            }

            val inFront = PhoneUsageReader(this@PhoneDownActivity).foregroundApp(now)
            val essentialInFront = inFront != null && app.essentials.isAllowed(inFront.first, inFront.second)
            if (essentialInFront) {
                handler.postDelayed(this, REOPEN_DELAY_MS)
            } else {
                Takeover.startFromBackground(this@PhoneDownActivity, PhoneDownLaunch.intent(this@PhoneDownActivity, intent.getBooleanExtra(EXTRA_POCKET, false)))
            }
        }
    }

    // Essentials / quick reply: the essentials screen takes over; the unlock was logged as a pickup when this screen came up.
    private fun toEssentials() {
        done = true
        startActivity(EssentialsActivity.intent(this))
        finish()
    }

    companion object {
        /** True when the phone was just in a pocket and "Unlock friction" is on: the options wait. */
        const val EXTRA_POCKET = "pocket"

        /** How long the options wait behind the unlock friction. */
        const val PENALTY_WAIT_SECONDS = 10L
        private const val REOPEN_DELAY_MS = 3_000L
        private const val CHECK_MS = 5_000L
    }
}

private enum class Mode { Main, Work, Emergency }

@Composable
private fun PhoneDownScreen(app: CompanionApp, pocket: Boolean, onPutAway: () -> Unit, onEssentials: () -> Unit) {
    // Back does nothing here: the ways out are the options (and the session ending).
    BackHandler {}
    var mode by remember { mutableStateOf(Mode.Main) }
    val shownAt = remember { Instant.now() }
    var now by remember { mutableStateOf(Instant.now()) }
    LaunchedEffect(Unit) {
        while (true) {
            now = Instant.now()
            delay(500)
        }
    }
    val waitLeft = if (pocket) (PhoneDownActivity.PENALTY_WAIT_SECONDS - Duration.between(shownAt, now).seconds).coerceAtLeast(0) else 0L
    // The pickups so far in the session, read off the main thread; null without Usage access.
    val sessionStart = remember { PlacementSession.current(app, Instant.now())?.start ?: EssentialsLaunch.realSessionStart(app, Instant.now())?.first }
    var pickups by remember { mutableStateOf<Int?>(null) }
    LaunchedEffect(Unit) {
        pickups = sessionStart?.let { start -> withContext(Dispatchers.Default) { app.sync.unlocksSince(start)?.size } }
    }

    androidx.compose.foundation.layout.Box(
        Modifier.fillMaxSize().background(Color.Black).statusBarsPadding().navigationBarsPadding().imePadding(),
    ) {
        Column(
            Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(horizontal = 28.dp, vertical = 24.dp),
            verticalArrangement = Arrangement.spacedBy(14.dp),
            horizontalAlignment = Alignment.CenterHorizontally,
        ) {
            Spacer(Modifier.height(24.dp))
            Icon(Icons.Outlined.Lock, contentDescription = null, tint = Color(0xFF7DD3C0), modifier = Modifier.size(56.dp))
            Text("Phones down", color = Color.White, fontSize = 34.sp)
            Text("In a bag or somewhere off you.", color = Color(0xCCFFFFFF), fontSize = 18.sp, textAlign = TextAlign.Center)
            timeLeftText(app, now)?.let { Text(it, color = Color(0xCCFFFFFF), fontSize = 16.sp, textAlign = TextAlign.Center) }
            pickups?.let { Text(if (it == 0) "Not picked up yet" else if (it == 1) "Picked up once so far" else "Picked up $it times so far", color = Color(0x99FFFFFF), fontSize = 14.sp) }
            Spacer(Modifier.height(8.dp))
            when {
                waitLeft > 0 -> {
                    // The phone just came out of a pocket: the options wait.
                    Text("Take a breath. Options in $waitLeft s.", color = Color(0xFFF4A261), fontSize = 18.sp, textAlign = TextAlign.Center)
                }

                mode == Mode.Work -> com.accountabilibuddy.companion.placement.WorkUseChooser(
                    app, onStarted = onEssentials, onBack = { mode = Mode.Main },
                )

                mode == Mode.Emergency -> EmergencyChooser(
                    app.emergency,
                    onStarted = {
                        app.refreshSoon()
                        onEssentials()
                    },
                    onBack = { mode = Mode.Main },
                )

                else -> {
                    Button(onClick = onPutAway, modifier = Modifier.fillMaxWidth()) {
                        Icon(Icons.Outlined.Lock, contentDescription = null, modifier = Modifier.size(20.dp))
                        Text("  Putting it away")
                    }
                    OutlinedButton(onClick = { mode = Mode.Work }, modifier = Modifier.fillMaxWidth()) {
                        Icon(Icons.Outlined.Work, contentDescription = null, modifier = Modifier.size(20.dp))
                        Text("  Need it for work")
                    }
                    OutlinedButton(onClick = onEssentials, modifier = Modifier.fillMaxWidth()) {
                        Icon(Icons.Outlined.Call, contentDescription = null, modifier = Modifier.size(20.dp))
                        Text("  Essentials / quick reply")
                    }
                    OutlinedButton(onClick = { mode = Mode.Emergency }, modifier = Modifier.fillMaxWidth()) {
                        Icon(Icons.Outlined.Warning, contentDescription = null, modifier = Modifier.size(20.dp))
                        Text("  Emergency")
                    }
                }
            }
        }
    }
}

// "Focus · 42m left · ends 3:15 PM" for the PC's running session; "Session running" without a set end.
private fun timeLeftText(app: CompanionApp, now: Instant): String? {
    val (_, end) = EssentialsLaunch.realSessionStart(app, now) ?: return null
    end ?: return "Session running"
    val minutes = Duration.between(now, end).toMinutes().coerceAtLeast(0)
    val left = if (minutes >= 60) "${minutes / 60}h ${"%02d".format(minutes % 60)}m" else "${minutes}m"
    val endsAt = end.atZone(ZoneId.systemDefault()).format(DateTimeFormatter.ofPattern("h:mm a", Locale.US))
    return "Focus · $left left · ends $endsAt"
}
