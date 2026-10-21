package com.accountabilibuddy.watch

import android.os.Bundle
import android.text.format.DateFormat
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.pager.HorizontalPager
import androidx.compose.foundation.pager.rememberPagerState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.unit.dp
import androidx.lifecycle.lifecycleScope
import com.accountabilibuddy.watch.link.PhoneLink
import com.accountabilibuddy.watch.model.SampleData
import com.accountabilibuddy.watch.ui.SessionPage
import com.accountabilibuddy.watch.ui.TodayPage
import com.accountabilibuddy.watch.ui.WeekPage
import com.accountabilibuddy.watch.ui.use24HourClock
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import java.time.LocalDateTime

/** The watch app (mockup): swipe between today's rings, the running session and the week. Sample data for now. */
class WatchActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        use24HourClock = DateFormat.is24HourFormat(this)
        setContent {
            MaterialTheme(colorScheme = darkColorScheme()) {
                WatchApp()
            }
        }
    }

    override fun onStart() {
        super.onStart()
        // Each time the app comes up, ask the phone for news (it answers over the Data Layer, ADR 21).
        lifecycleScope.launch { PhoneLink.askForNews(this@WatchActivity) }
    }
}

@Composable
private fun WatchApp() {
    val snapshot = remember { SampleData.snapshot() }
    var now by remember { mutableStateOf(LocalDateTime.now()) }
    LaunchedEffect(Unit) {
        while (true) {
            delay(1_000)
            now = LocalDateTime.now()
        }
    }

    val pager = rememberPagerState(pageCount = { 3 })
    Box(Modifier.fillMaxSize().background(Color.Black)) {
        HorizontalPager(pager, Modifier.fillMaxSize()) { page ->
            when (page) {
                0 -> TodayPage(snapshot, now)
                1 -> SessionPage(snapshot.running, now)
                else -> WeekPage(snapshot.week, now.toLocalDate())
            }
        }

        Row(
            Modifier.align(Alignment.BottomCenter).padding(bottom = 6.dp),
            horizontalArrangement = Arrangement.spacedBy(4.dp),
        ) {
            repeat(3) { index ->
                Box(
                    Modifier.size(5.dp).background(if (pager.currentPage == index) Color.White else Color(0x55FFFFFF), CircleShape),
                )
            }
        }
    }
}
