package com.accountabilibuddy.companion.guidance

import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.essentials.EssentialsLaunch
import com.accountabilibuddy.companion.placement.PlacementSession
import com.accountabilibuddy.shared.session.EndCostTerms
import com.accountabilibuddy.shared.session.SessionEndCostRules
import com.accountabilibuddy.shared.sync.SyncDeviceCard
import com.accountabilibuddy.shared.sync.SyncDocument
import com.accountabilibuddy.shared.sync.SyncFile
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.intOrNull
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import java.time.Instant

/**
 * What ending the running focus session early costs, so ProductaBuddy "now" during it costs the same (ADR 68): the PCs' end-early
 * wait and reason length (Settings › Ending a focus session early, read from the newest PC's profiles document), or nothing extra
 * when the session ends on command (no set end, a break, the phone's test session), as ending it does.
 *
 * PS2c (PS6a review S4): the phone's own session is NOT free in AccountabiliBuddy while it has a set end: it costs what ending it costs ([SessionEndCostRules.own]:
 * the phone's own wait and reason setting); ProductaBuddy and a session with no set end are free, as ending them is. With a PC session running too, the stricter.
 *
 * @property hasSetEnd False for a session with no set end: "after this session ends" means when it is ended by hand.
 */
data class SessionEndCost(val waitSeconds: Int, val minReasonLength: Int, val free: Boolean, val hasSetEnd: Boolean) {
    companion object {
        // The PC's defaults (BypassSettings.Default), for a PC whose profiles never reached this phone.
        private const val DEFAULT_WAIT_SECONDS = 60
        private const val DEFAULT_MIN_REASON = 15

        /** The running session's cost; null when no session runs. */
        fun now(app: CompanionApp, now: Instant = TrustedTime.now()): SessionEndCost? {
            val own = PlacementSession.ownRecord(app)?.let { SessionEndCostRules.own(EssentialsLaunch.mode(app), it, app.phoneSession.endSettings()) }
            val other = EssentialsLaunch.realSessionStart(app, now)?.let { pc -> pcCost(app, now, hasSetEnd = pc.second != null, isPcSession = true) }
                ?: app.testSession.running(now)?.let { test -> pcCost(app, now, hasSetEnd = test.second != null, isPcSession = false) }
            val cost = SessionEndCostRules.stricter(own, other) ?: return null
            return SessionEndCost(cost.waitSeconds, cost.minReasonLength, cost.free, cost.hasSetEnd)
        }

        // A PC session's cost (the PC's end-early rules); the phone's test session ends on command, so it is free.
        private fun pcCost(app: CompanionApp, now: Instant, hasSetEnd: Boolean, isPcSession: Boolean): EndCostTerms {
            val free = !isPcSession || !hasSetEnd || EssentialsLaunch.sessionOnBreak(app, now)
            val (wait, minReason) = pcRules(app.sync.received.value.map { it.file })
            return EndCostTerms(wait, minReason, free, hasSetEnd)
        }

        /** The newest PC profiles document's end-early rules: its "bypass" {waitDuration: "hh:mm:ss", minReasonLength}. */
        fun pcRules(files: List<SyncFile>): Pair<Int, Int> {
            val profiles = files
                .filter { it.device.kind == SyncDeviceCard.KIND_PC }
                .mapNotNull { file -> file.documents.firstOrNull { it.key == SyncDocument.PROFILES_KEY } }
                .maxByOrNull { it.editedAt }
                ?: return DEFAULT_WAIT_SECONDS to DEFAULT_MIN_REASON
            val bypass = runCatching { Json.parseToJsonElement(profiles.json).jsonObject["bypass"] as? JsonObject }.getOrNull()
                ?: return DEFAULT_WAIT_SECONDS to DEFAULT_MIN_REASON
            val wait = bypass["waitDuration"]?.jsonPrimitive?.content?.let(::seconds) ?: DEFAULT_WAIT_SECONDS
            val minReason = bypass["minReasonLength"]?.jsonPrimitive?.intOrNull ?: DEFAULT_MIN_REASON
            return wait to minReason
        }

        // .NET's TimeSpan text: "00:01:00", "1.02:00:00", fractions ignored.
        private fun seconds(text: String): Int? {
            val (days, clock) = if ('.' in text.substringBefore(':')) text.substringBefore('.').toIntOrNull() to text.substringAfter('.') else 0 to text
            val parts = clock.substringBefore('.').split(':').mapNotNull { it.toIntOrNull() }
            if (days == null || parts.size != 3) return null
            return days * 86_400 + parts[0] * 3_600 + parts[1] * 60 + parts[2]
        }
    }
}
