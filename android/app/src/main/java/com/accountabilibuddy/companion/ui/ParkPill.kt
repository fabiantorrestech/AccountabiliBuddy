package com.accountabilibuddy.companion.ui

import android.app.Activity
import android.content.ActivityNotFoundException
import android.content.Intent
import android.speech.RecognizerIntent
import android.widget.Toast
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.EditNote
import androidx.compose.material.icons.outlined.Mic
import androidx.compose.material3.Icon
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.OutlinedTextFieldDefaults
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardCapitalization
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.ui.kit.AbPrimaryButton
import com.accountabilibuddy.companion.ui.kit.AbQuietIconButton
import com.accountabilibuddy.companion.ui.kit.AbSectionLabel
import com.accountabilibuddy.companion.ui.kit.AbSheet
import com.accountabilibuddy.companion.ui.kit.AbTonalButton
import com.accountabilibuddy.companion.ui.theme.Ab

/**
 * The phone's own voice input, if it has one: the system's speech screen ([RecognizerIntent.ACTION_RECOGNIZE_SPEECH], offline
 * preferred). The app holds no microphone permission and opens no connection for it: the phone's voice service listens and hands
 * back text, which may travel through that service (it can be online). [available] is false when the phone has none (the
 * microphone is then not drawn). A cancelled screen does nothing. The text goes to [onResult] to be confirmed by the user; it is
 * never saved or sent from here.
 */
class VoiceInput(val available: Boolean, val launch: () -> Unit)

@Composable
fun rememberVoiceInput(onResult: (String) -> Unit): VoiceInput {
    val context = LocalContext.current
    val intent = remember {
        Intent(RecognizerIntent.ACTION_RECOGNIZE_SPEECH)
            .putExtra(RecognizerIntent.EXTRA_LANGUAGE_MODEL, RecognizerIntent.LANGUAGE_MODEL_FREE_FORM)
            .putExtra(RecognizerIntent.EXTRA_PREFER_OFFLINE, true)
            .putExtra(RecognizerIntent.EXTRA_MAX_RESULTS, 1)
    }
    val available = remember { intent.resolveActivity(context.packageManager) != null }
    val latest by rememberUpdatedState(onResult)
    val launcher = rememberLauncherForActivityResult(ActivityResultContracts.StartActivityForResult()) { result ->
        if (result.resultCode == Activity.RESULT_OK) {
            result.data?.getStringArrayListExtra(RecognizerIntent.EXTRA_RESULTS)?.firstOrNull()?.takeIf { it.isNotBlank() }?.let(latest)
        }
    }
    return VoiceInput(available) {
        try {
            launcher.launch(intent)
        } catch (_: ActivityNotFoundException) {
            // The voice service went away since the check: nothing to do.
        }
    }
}

/**
 * "Park a thought for later": the pill above the tab bar on Today and Tasks. It opens a text box and adds to the Come back to list
 * (the shared parked list, Keystore-sealed on this phone and never logged). The microphone on the pill speaks into the box; the
 * result waits there for the user to read and park, it never parks by itself.
 */
@Composable
fun ParkPill(modifier: Modifier = Modifier) {
    val context = LocalContext.current
    val app = context.applicationContext as CompanionApp
    var open by remember { mutableStateOf(false) }
    // Not remembered across a recreation on purpose: the draft is plain text and stays out of the saved instance state.
    var draft by remember { mutableStateOf("") }
    val voice = rememberVoiceInput { heard ->
        draft = if (draft.isBlank()) heard.trim() else draft.trimEnd() + " " + heard.trim()
        open = true
    }

    Surface(
        onClick = { open = true },
        modifier = modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 8.dp).heightIn(min = 52.dp),
        shape = Ab.shapes.pill,
        color = Ab.colors.surface2,
        contentColor = Ab.colors.muted,
    ) {
        Row(
            Modifier.padding(start = 16.dp, end = if (voice.available) 4.dp else 16.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            Icon(Icons.Outlined.EditNote, contentDescription = null, modifier = Modifier.size(22.dp))
            Text("Park a thought for later", style = Ab.type.body, color = Ab.colors.muted, maxLines = 1, modifier = Modifier.weight(1f))
            if (voice.available) AbQuietIconButton(Icons.Outlined.Mic, contentDescription = "Speak a thought", onClick = voice.launch)
        }
    }

    if (open) {
        ParkSheet(
            draft = draft,
            onDraft = { draft = it },
            voice = voice,
            onPark = {
                app.sharedLists.addParked(draft)
                draft = ""
                open = false
                Toast.makeText(context, "Parked", Toast.LENGTH_SHORT).show()
            },
            onDismiss = { open = false },
        )
    }
}

@Composable
private fun ParkSheet(draft: String, onDraft: (String) -> Unit, voice: VoiceInput, onPark: () -> Unit, onDismiss: () -> Unit) {
    val focus = remember { FocusRequester() }
    LaunchedEffect(Unit) { focus.requestFocus() }
    val colors = Ab.colors
    AbSheet(onDismiss = onDismiss, modifier = Modifier.imePadding()) {
        AbSectionLabel("Park a thought for later")
        OutlinedTextField(
            value = draft,
            onValueChange = onDraft,
            modifier = Modifier.fillMaxWidth().focusRequester(focus),
            placeholder = { Text("What should you come back to?", color = colors.faint) },
            minLines = 2,
            maxLines = 5,
            shape = Ab.shapes.mediumShape,
            keyboardOptions = KeyboardOptions(capitalization = KeyboardCapitalization.Sentences, imeAction = ImeAction.Done),
            keyboardActions = KeyboardActions(onDone = { if (draft.isNotBlank()) onPark() }),
            trailingIcon = if (voice.available) {
                { AbQuietIconButton(Icons.Outlined.Mic, contentDescription = "Speak a thought", onClick = voice.launch) }
            } else {
                null
            },
            colors = OutlinedTextFieldDefaults.colors(
                focusedTextColor = colors.ink, unfocusedTextColor = colors.ink,
                focusedBorderColor = colors.ink, unfocusedBorderColor = colors.line,
                cursorColor = colors.ink, focusedContainerColor = colors.surface, unfocusedContainerColor = colors.surface,
            ),
        )
        if (voice.available) {
            Text(
                "Voice uses your phone's voice service, which may be online. Typing stays on this phone.",
                style = Ab.type.small, color = colors.muted,
            )
        }
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            AbTonalButton("Cancel", onClick = onDismiss, modifier = Modifier.weight(1f))
            AbPrimaryButton("Park it", onClick = onPark, enabled = draft.isNotBlank(), modifier = Modifier.weight(1f))
        }
    }
}
