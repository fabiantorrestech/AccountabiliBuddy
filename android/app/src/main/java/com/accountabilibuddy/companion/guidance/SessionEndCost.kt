package com.accountabilibuddy.companion.guidance

import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.essentials.EssentialsLaunch
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
 * @property hasSetEnd False for a session with no set end: "after this session ends" means when it is ended by hand.
 */
data class SessionEndCost(val waitSeconds: Int, val minReasonLength: Int, val free: Boolean, val hasSetEnd: Boolean) {
    companion object {
        // The PC's defaults (BypassSettings.Default), for a PC whose profiles never reached this phone.
        private const val DEFAULT_WAIT_SECONDS = 60
        private const val DEFAULT_MIN_REASON = 15

        /** The running session's cost; null when no session runs. */
        fun now(app: CompanionApp, now: Instant = TrustedTime.now()): SessionEndCost? {
            val running = EssentialsLaunch.runningSessionStart(app, now) ?: return null
            val isPcSession = EssentialsLaunch.realSessionStart(app, now) != null
            val hasSetEnd = running.second != null
            val free = !isPcSession || !hasSetEnd || EssentialsLaunch.sessionOnBreak(app, now)
            val (wait, minReason) = pcRules(app.sync.received.value.map { it.file })
            return SessionEndCost(wait, minReason, free, hasSetEnd)
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
