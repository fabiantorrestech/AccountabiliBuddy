package com.accountabilibuddy.companion.model

import java.util.UUID

/** One allow or block rule inside a [FocusProfile]. */
data class Rule(
    val id: UUID,
    val target: MatchTarget,
    val kind: MatchKind,
    val pattern: String,
    val verdict: RuleVerdict,
    val note: String? = null,
)
