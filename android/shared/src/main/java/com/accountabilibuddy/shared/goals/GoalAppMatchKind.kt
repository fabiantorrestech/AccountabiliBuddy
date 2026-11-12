package com.accountabilibuddy.shared.goals

/** How one [GoalAppMatch] finds its usage rows (screen-time source v2). Twin of Core's `GoalAppMatchKind`. */
enum class GoalAppMatchKind {
    /** The phone's app id (`com.instagram.android`): exact, case-sensitive, phones only. */
    Package,

    /** The PC's process (`chrome.exe`): case-insensitive, ".exe" optional, PCs only. */
    Process,

    /** The name the device shows (`YouTube`): case-insensitive and exact, phones and PCs. */
    Name,

    /** A domain (`youtube.com`) and its subdomains, PCs only. */
    Website,
}
