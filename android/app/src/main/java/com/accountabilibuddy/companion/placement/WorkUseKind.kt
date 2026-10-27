package com.accountabilibuddy.companion.placement

/** What the phone is needed for during a session ("Need it for work"). */
enum class WorkUseKind(val label: String) {
    Call("Call"),
    TwoFactor("2FA"),
    WorkApp("Work app"),
    Other("Other"),
}
