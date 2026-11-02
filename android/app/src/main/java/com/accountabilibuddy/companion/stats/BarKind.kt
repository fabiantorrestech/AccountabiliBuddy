package com.accountabilibuddy.companion.stats

/** What a bar means, so the chart can pick its colour from the theme: [Met] the goal reached, [Accent] today still on its way, [Plan] the rest. */
enum class BarKind { Met, Accent, Plan, Drift, Sleep }
