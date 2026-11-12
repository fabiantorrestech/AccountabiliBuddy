package com.accountabilibuddy.companion.ui.today

import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.rememberScrollState
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.ui.kit.AbChip
import com.accountabilibuddy.companion.ui.kit.AbSectionLabel

/** One chip of More today: what it says, its icon, and what a tap opens (a half-height sheet, or the Noise picker). */
class MoreTodayChip(val key: String, val label: String, val icon: ImageVector, val onOpen: () -> Unit)

/**
 * "More today" on the phone (brief §4.1, CN §3): the parts the layout puts in More today, as a row of chips that scrolls sideways;
 * each opens a sheet over Today, so nothing on the page moves (CN §1 rule 2). Nothing is drawn with no chips.
 */
@Composable
fun MoreTodayChipRow(chips: List<MoreTodayChip>, modifier: Modifier = Modifier) {
    if (chips.isEmpty()) return
    Column(modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(8.dp)) {
        AbSectionLabel("More today")
        Row(Modifier.fillMaxWidth().horizontalScroll(rememberScrollState()), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            chips.forEach { chip -> AbChip(chip.label, selected = false, onClick = chip.onOpen, icon = chip.icon) }
        }
    }
}
