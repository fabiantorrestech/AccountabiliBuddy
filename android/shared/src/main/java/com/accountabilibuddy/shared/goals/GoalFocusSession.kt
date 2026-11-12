package com.accountabilibuddy.shared.goals

/**
 * One focus session as the goals see it ([GoalFocusAttribution]): its counted focus as local windows (time away already out)
 * and the tasks of its checklist, which say which label and #tag goals its minutes count toward.
 *
 * @property windows Its counted focus, local wall-clock time, earliest first.
 * @property tasks Its checklist's tasks; empty for none.
 * @property added Added by hand in History (ADR 20): counts toward goals, never toward a guard.
 */
data class GoalFocusSession(val windows: List<GoalWindow>, val tasks: List<GoalSessionTask>, val added: Boolean)
