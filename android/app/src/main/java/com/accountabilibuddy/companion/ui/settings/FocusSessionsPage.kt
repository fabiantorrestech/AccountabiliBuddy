package com.accountabilibuddy.companion.ui.settings

import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Info
import androidx.compose.runtime.Composable
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.ui.IconTitle
import com.accountabilibuddy.companion.ui.Note
import com.accountabilibuddy.companion.ui.PauseActions
import com.accountabilibuddy.companion.ui.SectionIcons
import com.accountabilibuddy.companion.ui.StartNudgeControls
import com.accountabilibuddy.companion.ui.TestSessionCard
import com.accountabilibuddy.companion.ui.kit.AbCard
import com.accountabilibuddy.companion.ui.kit.AbInfoBanner
import com.accountabilibuddy.companion.ui.theme.Ab

/**
 * Settings › Focus sessions: the work-start nudge (how often it comes again, and the full-screen work card), the test session (moved
 * from the bottom of Today; it tries the phone's settings without a real session) and a line saying where a session's length, its
 * breaks and Pomodoro are set: on the PC, since sessions start there.
 */
@Composable
fun FocusSessionsPage(anchor: String?, pauseActions: PauseActions) {
    SettingsPageList(
        listOf(
            PageRow(SettingsAnchors.START_NUDGE) {
                AbCard(verticalSpacing = 8.dp) {
                    IconTitle("Work-start nudge", style = Ab.type.section, icon = SectionIcons.FocusSessions, fontWeight = FontWeight.SemiBold)
                    Note("How often the work-start nudge comes again, and the full-screen card shown when you unlock during work time with no session.")
                    StartNudgeControls(pauseActions)
                }
            },
            PageRow(SettingsAnchors.TEST_SESSION) { TestSessionCard(asPage = true) },
            PageRow(SettingsAnchors.SESSION_LENGTHS) {
                AbInfoBanner("A session's length, its breaks and Pomodoro are set on the PC.", icon = Icons.Outlined.Info)
            },
        ),
        anchor,
    )
}
