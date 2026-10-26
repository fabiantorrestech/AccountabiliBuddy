package com.accountabilibuddy.shared.sync

/**
 * One of the device's blocks (ADR 37): a Curbox group or blocker on a phone, a Cold Turkey block on a PC, by [kind]
 * (a BlockRef kind), the blocker's own [id], its name, whether it's on, and whether its blocker keeps it as it is (a
 * locked Cold Turkey block). Names only, never what a block holds.
 */
data class SyncBlocker(val kind: String, val id: String, val name: String, val on: Boolean, val locked: Boolean = false)
