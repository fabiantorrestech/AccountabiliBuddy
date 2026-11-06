package com.accountabilibuddy.companion.ui

import android.app.Activity
import android.content.Context
import android.content.ContextWrapper
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.platform.LocalContext
import com.accountabilibuddy.companion.nfc.WorkTag
import com.accountabilibuddy.companion.nfc.WorkTagStore
import com.accountabilibuddy.companion.ui.kit.AbSectionLabel
import com.accountabilibuddy.companion.ui.theme.Ab
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

/**
 * The finished-work tag (ADR 67), under the work-start nudge's settings: set up (write a new secret to a tag and read it back),
 * test, and forget. The tag is read only while this page asks for it (and on the work card's "finished work" screen).
 */
@Composable
fun WorkTagControls() = GuardsScope {
    val context = LocalContext.current
    val activity = remember(context) { context.findActivity() }
    val store = remember { WorkTagStore(context) }
    val scope = rememberCoroutineScope()
    var isSet by remember { mutableStateOf(store.isSet) }
    // What the next tap does: write a new secret, test the tag, or nothing.
    var mode by remember { mutableStateOf(TagMode.Idle) }
    var pending by remember { mutableStateOf<String?>(null) }
    var status by remember { mutableStateOf<String?>(null) }

    AbSectionLabel("Finished-work tag")
    Text(
        "An NFC tag you keep where work ends, e.g. in your car. On the work card, \"I've finished work for the day\" then a tap ends today's " +
            "work at once on this phone, and on your PCs after their own wait. Without the tag it takes 10 minutes. The tag works only on that card.",
        style = Ab.type.small, color = Ab.colors.ink,
    )
    if (activity == null || !WorkTag.isAvailable(activity)) {
        Text("This phone has no NFC, so only the 10-minute wait is offered.", style = Ab.type.small, color = Ab.colors.ink)
        return@GuardsScope
    }

    DisposableEffect(mode) {
        if (mode != TagMode.Idle) {
            WorkTag.enable(activity) { tag ->
                when (mode) {
                    TagMode.Write -> {
                        val secret = pending ?: return@enable
                        status = "Writing…"
                        scope.launch {
                            val error = withContext(Dispatchers.IO) { WorkTag.write(tag, secret) }
                            if (error == null) {
                                store.save(secret)
                                isSet = true
                                status = "Done. Keep the tag where your work ends."
                                mode = TagMode.Idle
                            } else {
                                status = error
                            }
                        }
                    }
                    TagMode.Test -> {
                        status = if (store.matches(WorkTag.read(tag))) "That's your tag." else "That isn't your finished-work tag."
                        mode = TagMode.Idle
                    }
                    TagMode.Idle -> Unit
                }
            }
        }
        onDispose { WorkTag.disable(activity) }
    }

    if (!WorkTag.isOn(activity)) {
        Text("NFC is off in Android's settings: turn it on to set up or use the tag.", style = Ab.type.small, color = Ab.colors.ink)
    }
    when (mode) {
        TagMode.Write -> Text("Hold a writable tag (NTAG213, 215 or 216) to the back of the phone…", style = Ab.type.bodyMedium, color = Ab.colors.ink)
        TagMode.Test -> Text("Hold your tag to the back of the phone…", style = Ab.type.bodyMedium, color = Ab.colors.ink)
        TagMode.Idle -> Unit
    }
    status?.let { Text(it, style = Ab.type.small, color = Ab.colors.ink) }

    if (mode != TagMode.Idle) {
        GuardTonalButton("Stop", onClick = { mode = TagMode.Idle; status = null })
    } else {
        GuardTonalButton(if (isSet) "Set up a new tag" else "Set up a tag", onClick = {
            pending = store.newSecret()
            status = null
            mode = TagMode.Write
        })
        if (isSet) {
            GuardTonalButton("Test the tag", onClick = { status = null; mode = TagMode.Test })
            GuardTonalButton("Forget the tag", onClick = {
                store.forget()
                isSet = false
                status = "Forgotten. The tag no longer ends work; the 10-minute wait still does."
            })
        }
    }
}

private enum class TagMode { Idle, Write, Test }

private tailrec fun Context.findActivity(): Activity? = when (this) {
    is Activity -> this
    is ContextWrapper -> baseContext.findActivity()
    else -> null
}
