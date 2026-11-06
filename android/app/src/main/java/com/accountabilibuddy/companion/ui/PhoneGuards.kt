package com.accountabilibuddy.companion.ui

import android.content.Context
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.curbox.CurboxStatus
import com.accountabilibuddy.companion.essentials.EssentialsLaunch
import com.accountabilibuddy.companion.nudge.NudgeStore
import com.accountabilibuddy.companion.ui.kit.AbCard
import com.accountabilibuddy.companion.ui.theme.Ab
import java.time.LocalDate

/** One guardrail on this phone: its name, what it's doing now, and details. */
data class PhoneGuard(val name: String, val state: String, val details: List<String> = emptyList())

/**
 * What actually guards this phone (Profiles → this phone), read only: AccountabiliBuddy doesn't block on the phone,
 * so the PC's rules aren't shown here. Curbox (its API), the LockMyPhone-Extended hardcore profile, the essentials
 * screen, emergency phone time and the work card.
 */
object PhoneGuards {
    fun read(context: Context, curbox: CurboxStatus): List<PhoneGuard> {
        val app = context.applicationContext as CompanionApp
        return listOf(curbox(curbox), lockMyPhone(context, app), essentials(app), bedtime(app), emergency(app), workCard(context))
    }

    /** One line for the phone's row: what's on now. */
    fun summary(guards: List<PhoneGuard>): String =
        guards.filter { it.state.startsWith("On") || it.state.startsWith("Focus") }.joinToString(" · ") { it.name }
            .ifEmpty { "nothing running now" }

    private fun curbox(status: CurboxStatus): PhoneGuard = when {
        !status.installed -> PhoneGuard("Curbox", "Not installed")
        !status.granted -> PhoneGuard("Curbox", "Installed · not connected (Settings › Connections)")
        else -> PhoneGuard(
            "Curbox",
            if (status.focusActive) "Focus on" + (status.focusGroup?.let { " · $it" } ?: "") + (status.focusRemainingMinutes?.let { " · ${it}m left" } ?: "")
            else "Installed · no focus running",
            status.groups.map { group ->
                val on = when (group.active) { true -> " · on"; false -> " · off"; null -> "" }
                "${group.name} (${group.kind}, ${group.count} item${if (group.count == 1) "" else "s"})$on"
            },
        )
    }

    private fun lockMyPhone(context: Context, app: CompanionApp): PhoneGuard {
        val installed = runCatching { context.packageManager.getPackageInfo(EssentialsLaunch.LOCK_MY_PHONE, 0) }.isSuccess
        val profile = app.essentials.hardcoreProfile
        return when {
            !installed -> PhoneGuard("LockMyPhone-Extended", "Not installed")
            profile.isNullOrBlank() -> PhoneGuard("LockMyPhone-Extended", "Installed · no hardcore profile set (Guards › Phone guard › Essentials)")
            else -> PhoneGuard("LockMyPhone-Extended", "Hardcore profile \"$profile\" starts with each PC session",
                listOf("Its own blocks and allowed apps are set in LockMyPhone-Extended."))
        }
    }

    private fun essentials(app: CompanionApp): PhoneGuard {
        val store = app.essentials
        val list = store.essentials.value
        val shown = list.apps.count { !it.hidden }
        val hidden = list.apps.size - shown
        val folders = list.folders.count { it.shown }
        val details = listOf(
            "$shown app${if (shown == 1) "" else "s"} shown · $folders folder${if (folders == 1) "" else "s"} · $hidden hidden (search only)",
            if (store.onUnlockInSession) "Opens on unlock during PC sessions and emergency time" else "Opens on unlock during emergency time",
        )
        return PhoneGuard("Essentials screen", if (app.emergency.isActive()) "On (emergency time)" else "Ready", details)
    }

    private fun bedtime(app: CompanionApp): PhoneGuard {
        val store = app.bedtimeEssentials
        val lock = store.hardcoreProfile?.takeIf { app.essentials.useLockMyPhone }
        val details = listOfNotNull(
            "${store.essentials.value.apps.size} apps",
            if (store.onUnlockInSession) "Opens on unlock from bedtime until wake" else "Doesn't open by itself (Settings › Sleep)",
            lock?.let { "LockMyPhone-Extended \"$it\" locks the phone for the night" },
        )
        val state = when {
            !store.onUnlockInSession && lock == null -> "Off"
            com.accountabilibuddy.companion.essentials.BedtimeNight.current(app) != null -> "On now (the night)"
            else -> "Ready: from bedtime until wake"
        }
        return PhoneGuard("Bedtime essentials", state, details)
    }

    private fun emergency(app: CompanionApp): PhoneGuard {
        val store = app.emergency
        val left = store.minutesLeft(LocalDate.now())
        return PhoneGuard("Emergency phone time", if (store.isActive()) "On now · ${left}m left today" else "${left} of ${store.dailyMinutes}m left today")
    }

    private fun workCard(context: Context): PhoneGuard {
        val on = NudgeStore(context).load().takeoverOn
        return PhoneGuard("Work card", if (on) "Ready: covers the phone during work time with no session" else "Off (Settings › Focus sessions)")
    }
}

/** The guardrails as cards, under the phone's row in Profiles. */
@Composable
internal fun PhoneGuardCards(guards: List<PhoneGuard>, pcName: String?) {
    Column(Modifier.padding(start = 16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
        guards.forEach { guard ->
            AbCard(contentPadding = PaddingValues(12.dp), verticalSpacing = 2.dp) {
                Text(guard.name, style = Ab.type.bodyMedium, fontWeight = FontWeight.SemiBold, color = Ab.colors.ink)
                Text(guard.state, style = Ab.type.bodyMedium, color = Ab.colors.ink)
                guard.details.forEach { Note(it) }
            }
        }
        Note("${com.accountabilibuddy.companion.guidance.appBrandName()} doesn't block anything on the phone; these are what does." + (pcName?.let { " $it's profiles guard the PC." } ?: ""))
    }
}
