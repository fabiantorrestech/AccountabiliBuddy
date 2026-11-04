package com.accountabilibuddy.shared.sync

/** The colour editor's 24 presets, the same on the PC (`TaskCategory.Presets`), and the 8 at the start that a new category takes first. */
object TaskCategoryPresets {
    val PALETTE: List<String> = listOf("#2563EB", "#16A34A", "#DB2777", "#EA580C", "#7C3AED", "#0D9488", "#CA8A04", "#DC2626")

    val ALL: List<String> = PALETTE + listOf(
        "#0EA5E9", "#06B6D4", "#22C55E", "#84CC16", "#EAB308", "#F97316", "#EF4444", "#F43F5E",
        "#EC4899", "#D946EF", "#A855F7", "#6366F1", "#14B8A6", "#92400E", "#64748B", "#334155",
    )
}
