package com.accountabilibuddy.companion.ui.goals

import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.outlined.DirectionsRun
import androidx.compose.material.icons.automirrored.outlined.DirectionsWalk
import androidx.compose.material.icons.outlined.Bedtime
import androidx.compose.material.icons.outlined.Book
import androidx.compose.material.icons.outlined.CenterFocusStrong
import androidx.compose.material.icons.outlined.Code
import androidx.compose.material.icons.outlined.FitnessCenter
import androidx.compose.material.icons.outlined.Flag
import androidx.compose.material.icons.outlined.Home
import androidx.compose.material.icons.outlined.MonitorWeight
import androidx.compose.material.icons.outlined.MusicNote
import androidx.compose.material.icons.outlined.Palette
import androidx.compose.material.icons.outlined.Savings
import androidx.compose.material.icons.outlined.School
import androidx.compose.material.icons.outlined.SelfImprovement
import androidx.compose.material.icons.outlined.Timer
import androidx.compose.material.icons.outlined.TrackChanges
import androidx.compose.material.icons.outlined.Translate
import androidx.compose.material.icons.outlined.WaterDrop
import androidx.compose.material.icons.outlined.Work
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector

/**
 * A goal's or area's stored icon name (Material Symbols style, "fitness_center") as a phone icon (ADR 32), with the same meanings as
 * the PC's `GoalIcons`; an unknown name is the Goals icon (`track_changes`). And its user-picked colour, which tints the icon chip
 * and the visuals, never text on its own (brief §3.9); an unreadable colour is null (the caller uses the theme's accent).
 */
object GoalIcons {
    /** The Goals tab and page (brief §3.10). */
    val Goals: ImageVector = Icons.Outlined.TrackChanges

    private val known: Map<String, ImageVector> = mapOf(
        "work" to Icons.Outlined.Work,
        "center_focus_strong" to Icons.Outlined.CenterFocusStrong,
        "timer" to Icons.Outlined.Timer,
        "flag" to Icons.Outlined.Flag,
        "fitness_center" to Icons.Outlined.FitnessCenter,
        "monitor_weight" to Icons.Outlined.MonitorWeight,
        "scale" to Icons.Outlined.MonitorWeight,
        "directions_run" to Icons.AutoMirrored.Outlined.DirectionsRun,
        "directions_walk" to Icons.AutoMirrored.Outlined.DirectionsWalk,
        "footprint" to Icons.AutoMirrored.Outlined.DirectionsWalk,
        "steps" to Icons.AutoMirrored.Outlined.DirectionsWalk,
        "school" to Icons.Outlined.School,
        "menu_book" to Icons.Outlined.Book,
        "book" to Icons.Outlined.Book,
        "translate" to Icons.Outlined.Translate,
        "home" to Icons.Outlined.Home,
        "self_improvement" to Icons.Outlined.SelfImprovement,
        "water_drop" to Icons.Outlined.WaterDrop,
        "bedtime" to Icons.Outlined.Bedtime,
        "code" to Icons.Outlined.Code,
        "music_note" to Icons.Outlined.MusicNote,
        "palette" to Icons.Outlined.Palette,
        "savings" to Icons.Outlined.Savings,
        "track_changes" to Icons.Outlined.TrackChanges,
    )

    fun of(name: String?): ImageVector = name?.lowercase()?.let(known::get) ?: Goals

    /** "#RRGGBB" (or "#AARRGGBB") as a colour; null when it can't be read. */
    fun color(text: String?): Color? {
        val hex = text?.trim()?.removePrefix("#") ?: return null
        val value = hex.toLongOrNull(16) ?: return null
        return when (hex.length) {
            6 -> Color(0xFF000000 or value)
            8 -> Color(value)
            else -> null
        }
    }
}
