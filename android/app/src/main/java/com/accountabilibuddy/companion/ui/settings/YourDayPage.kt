package com.accountabilibuddy.companion.ui.settings

import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.ui.LunchSection
import com.accountabilibuddy.companion.ui.SectionIcons
import com.accountabilibuddy.companion.ui.kit.AbCard
import com.accountabilibuddy.companion.ui.kit.AbListRow

/** Settings › Your day: lunch, and a link to Plan for work hours and day types. */
@Composable
fun YourDayPage(anchor: String?, onOpenPlan: () -> Unit) {
    val app = LocalContext.current.applicationContext as CompanionApp
    val lunchLength by app.lunch.lengthMinutes.collectAsState()
    SettingsPageList(
        listOf(
            PageRow(SettingsAnchors.LUNCH) { LunchSection(lunchLength) },
            PageRow(SettingsAnchors.WORK_HOURS) {
                AbCard(contentPadding = PaddingValues(horizontal = 14.dp, vertical = 4.dp), verticalSpacing = 0.dp) {
                    AbListRow(
                        "Work hours and day types",
                        icon = SectionIcons.Plan,
                        subtitle = "Work hours, day types, day parts and days off are edited in Plan",
                        onClick = onOpenPlan,
                    )
                }
            },
        ),
        anchor,
    )
}
