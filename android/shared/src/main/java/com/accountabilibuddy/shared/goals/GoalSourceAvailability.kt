package com.accountabilibuddy.shared.goals

/** Where a source can be measured today ([GoalSources.availabilityOf]), for the greyed and "Measured on" texts. */
enum class GoalSourceAvailability {
    /** Counted the same on every device from what it already holds. */
    Everywhere,

    /** Counted on the PC only; the phone shows "Measured on your PC". */
    PcOnly,

    /** Counted on the phone only; the PC shows "Measured on your phone" (until ADR 79 lets the derived day value travel). */
    PhoneOnly,

    /** Drawn greyed, "Coming soon", not clickable: it needs an ADR that isn't accepted yet. */
    ComingSoon,
}
