package com.accountabilibuddy.companion.guidance

import android.content.Context
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.ui.platform.LocalContext
import com.accountabilibuddy.companion.CompanionApp

/**
 * The name the app shows for itself (ADR 68): AccountabiliBuddy while guarding, ProductaBuddy in companion mode. The About page and
 * names that must match something outside the app (the launcher label, the PC's extension, file names) keep [PRODUCT].
 */
object AppBrand {
    const val PRODUCT = "AccountabiliBuddy"
    const val COMPANION = "ProductaBuddy"

    fun of(mode: GuidanceMode): String = if (mode == GuidanceMode.Companion) COMPANION else PRODUCT

    /** The name now, for text built outside Compose (notifications). */
    fun name(context: Context): String = of((context.applicationContext as CompanionApp).guidance.state.value.mode)
}

/** ProductaBuddy is in effect, in Compose: follows the mode live. */
@Composable
fun isCompanionMode(): Boolean {
    val app = LocalContext.current.applicationContext as CompanionApp
    return app.guidance.state.collectAsState().value.mode == GuidanceMode.Companion
}

/** The name now, in Compose: follows the mode live. */
@Composable
fun appBrandName(): String {
    val app = LocalContext.current.applicationContext as CompanionApp
    val state = app.guidance.state.collectAsState()
    return AppBrand.of(state.value.mode)
}
