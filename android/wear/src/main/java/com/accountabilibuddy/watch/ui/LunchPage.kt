package com.accountabilibuddy.watch.ui

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.wear.compose.material3.Button
import androidx.wear.compose.material3.IconButton
import androidx.wear.compose.material3.Text
import com.accountabilibuddy.shared.LunchRemote
import com.accountabilibuddy.shared.WatchLunch
import com.accountabilibuddy.shared.WatchText
import com.accountabilibuddy.watch.link.LunchLink
import kotlinx.coroutines.launch
import java.time.Duration
import java.time.Instant
import java.time.LocalTime
import java.time.ZoneId

/**
 * Today's flexible lunch (from the phone): "Start lunch" for the phone's lunch length; while it runs, a ring that
 * empties as it goes with the time left in the middle, −5 / +5 min and "I'm back"; then when it was. The buttons go to
 * the phone, which tells the other devices.
 */
@Composable
fun LunchPage(lunch: WatchLunch, now: Instant, zone: ZoneId) {
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    var unreachable by remember { mutableStateOf(false) }
    fun send(action: String) = scope.launch { unreachable = !LunchLink.send(context, action) }
    fun clock(at: Instant): String = WatchText.clock(LocalTime.ofInstant(at, zone), use24HourClock)

    val running = lunch.isRunning(now)
    Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
        Canvas(Modifier.fillMaxSize()) {
            val ring = 10.dp.toPx()
            val radius = size.minDimension / 2 - 8.dp.toPx() - ring / 2
            ringTrack(radius, ring, Color(0x26FFFFFF))
            if (running) {
                progressArc(lunch.fractionLeft(now), radius, ring, Color(LUNCH_COLOR))
            }
        }

        Column(horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(4.dp)) {
            when {
                running -> {
                    val end = lunch.end!!
                    Text("Lunch", color = Color(LUNCH_COLOR), fontSize = 13.sp, fontWeight = FontWeight.Medium)
                    Text(WatchText.timer(Duration.between(now, end)), color = Color.White, fontSize = 28.sp, fontWeight = FontWeight.SemiBold)
                    Text("until ${clock(end)}", color = Color(0xB3FFFFFF), fontSize = 11.sp)
                    Row(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalAlignment = Alignment.CenterVertically) {
                        IconButton(onClick = { send(LunchRemote.MINUS) }, enabled = lunch.canShorten(now), modifier = Modifier.size(40.dp)) {
                            Text("−5", fontSize = 14.sp)
                        }
                        IconButton(onClick = { send(LunchRemote.PLUS) }, modifier = Modifier.size(40.dp)) { Text("+5", fontSize = 14.sp) }
                    }
                    Button(onClick = { send(LunchRemote.BACK) }) { Text("I'm back", fontSize = 13.sp) }
                }
                lunch.isDone(now) -> {
                    Text("Lunch done", color = Color(LUNCH_COLOR), fontSize = 14.sp, fontWeight = FontWeight.Medium)
                    Text("${clock(lunch.start!!)} – ${clock(lunch.end!!)}", color = Color(0xB3FFFFFF), fontSize = 12.sp)
                }
                else -> {
                    Text("Lunch · ${WatchText.duration(lunch.lengthMinutes)}", color = Color(LUNCH_COLOR), fontSize = 14.sp, fontWeight = FontWeight.Medium)
                    Button(onClick = { send(LunchRemote.START) }) { Text("Start lunch", fontSize = 13.sp) }
                }
            }
            if (unreachable) {
                Text(
                    "Can't reach your phone", color = Color(0xFFFCD34D), fontSize = 10.sp, textAlign = TextAlign.Center,
                    modifier = Modifier.padding(horizontal = 24.dp),
                )
            }
        }
    }
}

/** The day plan's Lunch colour (amber), as on the phone and the PC. */
private const val LUNCH_COLOR = 0xFFCA8A04
