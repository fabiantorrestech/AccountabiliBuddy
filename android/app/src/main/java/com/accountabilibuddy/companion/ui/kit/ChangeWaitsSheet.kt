package com.accountabilibuddy.companion.ui.kit

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Row
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Schedule
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.AnnotatedString
import androidx.compose.ui.text.SpanStyle
import androidx.compose.ui.text.buildAnnotatedString
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.withStyle
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.ui.theme.Ab
import java.time.Instant
import java.time.ZoneId
import java.time.format.DateTimeFormatter
import java.util.Locale

private val CLOCK: DateTimeFormatter = DateTimeFormatter.ofPattern("h:mm a", Locale.US)

/**
 * The "change waits" sheet (board 5h): a looser change that can't apply at once waits its time and then applies on its own.
 * It says what is being changed, the wait and the moment it applies ([appliesAt], 12-hour); there is no "apply now" and
 * nothing here shortens a wait. Reuse it only for waits that already exist (a plan edit, placement or a pocket penalty off,
 * sleep off); it never adds one.
 *
 * Two shapes: with [onQueue] it asks first (the accent "Queue it" starts the wait the caller already has; the quiet "Never
 * mind" leaves everything as it was); without it the wait has already started and the sheet only says so (one quiet button).
 * [cancelNote] finishes the "Applies at ..." line, e.g. "cancel any time before" or "cancel it on Work PC before then";
 * [about] says "about" when the time is worked out from another device's file time.
 */
@Composable
fun ChangeWaitsSheet(
    title: String,
    body: String,
    appliesAt: Instant,
    onDismiss: () -> Unit,
    modifier: Modifier = Modifier,
    onQueue: (() -> Unit)? = null,
    queueText: String = "Queue it",
    dismissText: String = if (onQueue != null) "Never mind" else "Got it",
    cancelNote: String = "cancel any time before",
    about: Boolean = false,
) {
    AbSheet(onDismiss, modifier) {
        Text(title, style = Ab.type.head, color = Ab.colors.ink)
        Text(body, style = Ab.type.bodyMedium, color = Ab.colors.muted)
        AbInfoBanner(appliesLine(appliesAt, cancelNote, about), icon = Icons.Outlined.Schedule)
        Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
            if (onQueue != null) {
                AbPrimaryButton(queueText, onClick = { onQueue(); onDismiss() }, modifier = Modifier.weight(1f))
            }
            AbTonalButton(dismissText, onClick = onDismiss, modifier = Modifier.weight(1f))
        }
    }
}

// "Applies at 1:48 PM · cancel any time before", the time in bold.
private fun appliesLine(at: Instant, cancelNote: String, about: Boolean): AnnotatedString = buildAnnotatedString {
    append(if (about) "Applies at about " else "Applies at ")
    withStyle(SpanStyle(fontWeight = FontWeight.Bold)) { append(CLOCK.format(at.atZone(ZoneId.systemDefault()))) }
    append(" · $cancelNote")
}
