package com.accountabilibuddy.companion.ui.settings

import androidx.compose.runtime.Composable
import com.accountabilibuddy.companion.ui.TestSessionCard

/**
 * Settings › Focus sessions. The test session (moved from the bottom of Today) tries the phone's settings without a real session;
 * the start nudge and the rest come with a later part.
 */
@Composable
fun FocusSessionsPage(anchor: String?) {
    SettingsPageList(listOf(PageRow(SettingsAnchors.TEST_SESSION) { TestSessionCard() }), anchor)
}
