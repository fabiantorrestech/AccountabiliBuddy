package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.ui.kit.AbChip
import com.accountabilibuddy.companion.ui.kit.AbChipRow
import com.accountabilibuddy.companion.ui.theme.Ab

/**
 * The plan's weeks repeating in a cycle of more than one week (schema v13, ADR 47): "This is week N of M". Picking another week
 * is a day plan edit sent to the PC (it moves week 1's start by whole weeks); the PC applies it at once when it adds work, and
 * holds it when it frees today's work. It is the body of a sheet opened from Plan's ⋮ menu.
 */
@Composable
fun ThisWeekCard(weekNumber: Int, weekCount: Int, onPick: (week: Int) -> Unit) {
    Column(verticalArrangement = Arrangement.spacedBy(10.dp)) {
        Text("This is week $weekNumber of $weekCount", style = Ab.type.head, color = Ab.colors.ink)
        AbChipRow {
            for (week in 1..weekCount) {
                AbChip("$week", selected = week == weekNumber, onClick = { if (week != weekNumber) onPick(week) })
            }
        }
        Text(
            "Sent to the PC like any plan edit; a switch that frees today's work waits there.",
            style = Ab.type.small,
            color = Ab.colors.muted,
        )
    }
}
