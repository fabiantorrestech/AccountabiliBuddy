package com.accountabilibuddy.companion.link

import com.accountabilibuddy.companion.data.JsonValues.bool
import com.accountabilibuddy.companion.data.JsonValues.string
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject
import java.time.OffsetDateTime

/** The PC's last state and when it came, kept while the link is idle (the watch still shows what runs, ADR 21). */
data class PcStateAt(val state: PcState, val at: java.time.Instant)

/** A profile the phone can start on the PC. */
data class PcProfile(val id: String, val name: String)

/**
 * The PC's session as it sent it (times are absolute, so the phone counts down by itself between updates).
 * [mode] is "manual", "pomodoro" or "scheduled", null when nothing runs; scheduled blocks block but aren't focus.
 */
data class PcState(
    val mode: String?,
    val phase: String,
    val countsAsFocus: Boolean,
    val profileId: String?,
    val profileName: String?,
    val phaseStartedAt: OffsetDateTime?,
    val phaseEndsAt: OffsetDateTime?,
    val profiles: List<PcProfile>,
    val countUpProfileId: String?,
) {
    val isActive: Boolean get() = mode != null
    val isBreak: Boolean get() = phase == "shortBreak" || phase == "longBreak"
    val isScheduledBlock: Boolean get() = mode == "scheduled"

    companion object {
        fun parse(body: JsonObject): PcState = PcState(
            mode = body.string("mode"),
            phase = body.string("phase") ?: "idle",
            countsAsFocus = body.bool("countsAsFocus", false),
            profileId = body.string("profileId"),
            profileName = body.string("profileName"),
            phaseStartedAt = body.string("phaseStartedAt")?.let { runCatching { OffsetDateTime.parse(it) }.getOrNull() },
            phaseEndsAt = body.string("phaseEndsAt")?.let { runCatching { OffsetDateTime.parse(it) }.getOrNull() },
            profiles = (body["profiles"] as? JsonArray).orEmpty().mapNotNull { element ->
                val profile = element as? JsonObject ?: return@mapNotNull null
                val id = profile.string("id") ?: return@mapNotNull null
                PcProfile(id, profile.string("name") ?: "(no name)")
            },
            countUpProfileId = body.string("countUpProfileId"),
        )
    }
}
