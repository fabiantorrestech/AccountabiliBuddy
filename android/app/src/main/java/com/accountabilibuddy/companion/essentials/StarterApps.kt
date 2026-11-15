package com.accountabilibuddy.companion.essentials

import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.provider.AlarmClock
import android.provider.Telephony
import android.telecom.TelecomManager

/** One suggested starter app for a list: [ticked] ones are pre-selected on the setup page. */
data class StarterApp(val packageName: String, val role: String, val ticked: Boolean)

/**
 * Starter essentials for the setup's "When it engages" page (ADR 73), from this phone's own default apps rather than a
 * fixed list of Google apps: the dialer, messages, clock, calendar and maps by their roles, an installed authenticator,
 * and for the night a music app (suggested, not ticked). Only installed apps are offered.
 */
object StarterApps {
    /** Authenticator apps people commonly use; the first installed one is offered for focus. */
    private val AUTHENTICATORS = listOf(
        "com.google.android.apps.authenticator2",
        "com.azure.authenticator",
        "com.authy.authy",
        "com.twofasapp",
        "com.beemdevelopment.aegis",
    )

    fun focus(context: Context): List<StarterApp> = listOfNotNull(
        dialer(context)?.let { StarterApp(it, "Phone", ticked = true) },
        sms(context)?.let { StarterApp(it, "Messages", ticked = true) },
        clock(context)?.let { StarterApp(it, "Clock", ticked = true) },
        byCategory(context, Intent.CATEGORY_APP_CALENDAR)?.let { StarterApp(it, "Calendar", ticked = true) },
        byCategory(context, Intent.CATEGORY_APP_MAPS)?.let { StarterApp(it, "Maps", ticked = true) },
        AUTHENTICATORS.firstOrNull { installed(context, it) }?.let { StarterApp(it, "Authenticator", ticked = true) },
    ).distinctBy { it.packageName }

    fun night(context: Context): List<StarterApp> = listOfNotNull(
        dialer(context)?.let { StarterApp(it, "Phone", ticked = true) },
        sms(context)?.let { StarterApp(it, "Messages", ticked = true) },
        clock(context)?.let { StarterApp(it, "Clock (the alarm)", ticked = true) },
        byCategory(context, Intent.CATEGORY_APP_MUSIC)?.let { StarterApp(it, "Music or sleep sounds", ticked = false) },
    ).distinctBy { it.packageName }

    private fun dialer(context: Context): String? =
        runCatching { context.getSystemService(TelecomManager::class.java)?.defaultDialerPackage }.getOrNull()

    private fun sms(context: Context): String? = runCatching { Telephony.Sms.getDefaultSmsPackage(context) }.getOrNull()

    private fun clock(context: Context): String? = resolve(context, Intent(AlarmClock.ACTION_SHOW_ALARMS))

    private fun byCategory(context: Context, category: String): String? =
        resolve(context, Intent.makeMainSelectorActivity(Intent.ACTION_MAIN, category))

    // The app that answers [intent] by default; null when none does, or only the "choose an app" screen would.
    private fun resolve(context: Context, intent: Intent): String? = runCatching {
        context.packageManager.resolveActivity(intent, PackageManager.MATCH_DEFAULT_ONLY)?.activityInfo?.packageName
    }.getOrNull()?.takeIf { it != "android" && installed(context, it) }

    private fun installed(context: Context, packageName: String): Boolean =
        runCatching { context.packageManager.getPackageInfo(packageName, 0) }.isSuccess
}
