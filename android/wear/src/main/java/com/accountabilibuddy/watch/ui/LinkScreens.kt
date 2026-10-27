package com.accountabilibuddy.watch.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.wear.compose.material3.Button
import androidx.wear.compose.material3.OutlinedButton
import androidx.wear.compose.material3.Text
import com.accountabilibuddy.shared.WatchText

/** Before the phone has sent anything: asking, can't reach the phone, or not linked; and a sample day to look at. */
@Composable
fun NotLinkedScreen(phoneReachable: Boolean?, onAskAgain: () -> Unit, onSample: () -> Unit) {
    val (title, line) = WatchText.notLinked(phoneReachable)
    Box(Modifier.fillMaxSize().padding(horizontal = 24.dp), contentAlignment = Alignment.Center) {
        Column(horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(6.dp)) {
            Text(title, color = Color.White, fontSize = 15.sp, fontWeight = FontWeight.SemiBold, textAlign = TextAlign.Center)
            Text(line, color = Color(0xB3FFFFFF), fontSize = 12.sp, textAlign = TextAlign.Center)
            if (phoneReachable != null) {
                Button(onClick = onAskAgain) { Text("Try again") }
                OutlinedButton(onClick = onSample) { Text("Sample day") }
            }
        }
    }
}

/**
 * The last page: where the data comes from and how old it is, and forgetting it on this watch (the phone sends it
 * again while the watch is linked; unlinking is on the phone). A sample day has a way back instead.
 */
@Composable
fun LinkPage(pcName: String?, updated: String?, isSample: Boolean, onForget: () -> Unit, sources: List<String> = emptyList()) {
    Box(Modifier.fillMaxSize().padding(horizontal = 24.dp), contentAlignment = Alignment.Center) {
        Column(horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(6.dp)) {
            if (isSample) {
                Text("Sample day", color = Color.White, fontSize = 15.sp, fontWeight = FontWeight.SemiBold)
                Text("Made-up data to look around.", color = Color(0xB3FFFFFF), fontSize = 12.sp, textAlign = TextAlign.Center)
                Button(onClick = onForget) { Text("Back") }
                return@Column
            }

            Text("Linked to your phone", color = Color.White, fontSize = 14.sp, fontWeight = FontWeight.SemiBold, textAlign = TextAlign.Center)
            if (sources.size > 1) {
                // Focus from several devices (ADR 22): each with how old its data is.
                Text("Focus from", color = Color(0xB3FFFFFF), fontSize = 12.sp)
                sources.forEach { line -> Text(line, color = Color(0xB3FFFFFF), fontSize = 11.sp, textAlign = TextAlign.Center) }
            } else {
                Text(pcName?.let { "Focus from $it" } ?: "No PC linked to the phone", color = Color(0xB3FFFFFF), fontSize = 12.sp, textAlign = TextAlign.Center)
                Text(updated ?: "Up to date", color = Color(0xB3FFFFFF), fontSize = 11.sp)
            }
            OutlinedButton(onClick = onForget) { Text("Forget data") }
        }
    }
}
