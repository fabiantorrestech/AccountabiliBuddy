package com.accountabilibuddy.companion.ui.settings

import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.ui.platform.LocalContext
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.ui.LunchSection
import com.accountabilibuddy.companion.ui.Section
import com.accountabilibuddy.companion.ui.theme.Ab

/** Settings › Your day: lunch, and a pointer to Plan for work hours and day types. */
@Composable
fun YourDayPage(anchor: String?, onOpenPlan: () -> Unit) {
    val app = LocalContext.current.applicationContext as CompanionApp
    val lunchLength by app.lunch.lengthMinutes.collectAsState()
    SettingsPageList(
        listOf(
            PageRow(SettingsAnchors.LUNCH) { LunchSection(lunchLength) },
            PageRow(SettingsAnchors.WORK_HOURS) {
                Section("Work hours and day types", summary = "Edited in Plan") {
                    Text("Your work hours, day types, day parts and days off are in Plan.", style = MaterialTheme.typography.bodySmall)
                    TextButton(onClick = onOpenPlan, colors = ButtonDefaults.textButtonColors(contentColor = Ab.colors.ink)) { Text("Open Plan") }
                }
            },
        ),
        anchor,
    )
}
