package com.accountabilibuddy.companion.ui

import com.accountabilibuddy.companion.sync.JoinLink

/**
 * A join link waiting for the user's answer (ADR 22, 46). A link can come from anywhere (a camera, a message, a web
 * page, another app), so nothing in it is trusted and nothing happens until the user has seen what it would change.
 *
 * @property link What the link says; its name is only a label somebody typed.
 * @property leavesCurrentGroup Joining would leave the device group this phone is in now (its other devices stop syncing
 *   with this phone and their copies are removed from it).
 * @property newRelay The link's relay is not the one this phone syncs through now.
 * @property relayOnInternet The link's relay is not on a local network (not a home, office or VPN address).
 * @property confirmingLeave The second question, "Really leave your current group?", is showing.
 */
data class JoinPrompt(
    val link: JoinLink,
    val leavesCurrentGroup: Boolean,
    val newRelay: Boolean,
    val relayOnInternet: Boolean,
    val confirmingLeave: Boolean = false,
)
