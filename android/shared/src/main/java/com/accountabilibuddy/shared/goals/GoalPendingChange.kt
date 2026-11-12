package com.accountabilibuddy.shared.goals

import java.time.OffsetDateTime
import kotlinx.serialization.json.JsonObject

/**
 * A looser target waiting for its delay (brief §5.7; [GoalLowering]). It applies once [appliesAt] has passed and
 * [GoalLowering.canApply] says so (never during a session outside ProductaBuddy mode).
 *
 * @property amount The new amount; null = no target any more ("Track time only").
 * @property appliesAt The earliest it may apply.
 * @property madeInProductaBuddy Made in ProductaBuddy mode, where it skipped the delay: in AccountabiliBuddy mode it waits the
 * full delay first ([GoalLowering.intoAccountabiliBuddy]). Left out of the JSON when false.
 * @property extra Fields written by a newer app, kept and written back unchanged after the known ones (ADR 79 forward compatibility);
 * empty when there are none. May hold private text: sealed at rest, never logged.
 */
data class GoalPendingChange(
    val amount: Double?,
    val appliesAt: OffsetDateTime,
    val madeInProductaBuddy: Boolean = false,
    val extra: JsonObject = JsonObject(emptyMap()),
)
