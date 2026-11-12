package com.accountabilibuddy.shared.goals

import java.util.UUID

/**
 * What goals read of a synced session's checklist task (the PC's `SessionTask`, plan §3b 5: nothing new): its label as the
 * category (its id, or the name an older device sends) and its #tags (the ADR 72 labels). Titles and notes are never read.
 */
data class GoalSessionTask(val categoryId: UUID?, val category: String?, val labels: List<String>?)
