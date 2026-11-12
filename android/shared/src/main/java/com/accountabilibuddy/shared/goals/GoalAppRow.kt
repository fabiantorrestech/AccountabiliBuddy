package com.accountabilibuddy.shared.goals

/**
 * One device usage row for [GoalAppMatching]: [id] is the phone's package or the PC's process name, [label] the name the device
 * shows (or null), [site] the domain of a PC site row (or null).
 */
data class GoalAppRow(val id: String, val label: String?, val site: String?, val minutes: Double)
