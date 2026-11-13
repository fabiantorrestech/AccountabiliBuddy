package com.accountabilibuddy.companion.ui.settings

import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.padding
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Computer
import androidx.compose.material.icons.outlined.PhoneAndroid
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.tasks.TaskSettingsActions
import com.accountabilibuddy.companion.ui.kit.AbCard
import com.accountabilibuddy.companion.ui.kit.AbListRow
import com.accountabilibuddy.companion.ui.kit.AbSectionLabel
import com.accountabilibuddy.companion.ui.kit.AbToggleRow
import com.accountabilibuddy.companion.ui.theme.Ab
import com.accountabilibuddy.shared.tasks.TaskRingDevices

/**
 * Settings › Alerts & on-screen › Task reminders (R7.4; the PC's Alerts › Task reminders › "Task reminders ring on"): one switch per
 * phone of the group for the account's "Rings on" list, PCs shown locked on (a PC always rings for its own reminders). The account's
 * list is shared with every device; a label's own list (Settings › Tasks › Labels) narrows it. Saved at once, stamped by this phone.
 * The phone sets no task alarm yet: its reminders show on the Tasks page and say whether this phone is on both lists.
 */
@Composable
fun TaskRemindersCard() {
    val app = LocalContext.current.applicationContext as CompanionApp
    val actions = remember { TaskSettingsActions(app) }
    val lists by app.taskModel.lists.collectAsStateWithLifecycle()
    val received by app.sync.received.collectAsStateWithLifecycle()
    val ringOn = lists.account?.ringOn
    // Read again when a device joins or leaves.
    val phones = remember(received) { actions.phones() }
    val pcs = remember(received) { actions.pcNames() }
    AbCard(contentPadding = PaddingValues(horizontal = 14.dp, vertical = 10.dp), verticalSpacing = 0.dp) {
        AbSectionLabel("Task reminders · rings on", Modifier.padding(start = 4.dp, bottom = 4.dp))
        pcs.forEach { pc -> AbListRow(pc, icon = Icons.Outlined.Computer, subtitle = "Always rings for its own reminders", showChevron = false, locked = false) }
        phones.forEach { phone ->
            AbToggleRow(
                phone.name, TaskRingDevices.rings(ringOn, phone.id), { on -> actions.setAccountRing(phone.id, on) },
                subtitle = if (phone.isThisPhone) "This phone" else "Phone",
            )
        }
        Text(
            "Which devices ring for task reminders. A label can narrow this in Settings › Tasks › Labels. A device linked later rings too, " +
                "until you turn it off here. The phone shows its reminders on the Tasks page and says whether it rings; a phone alarm for them comes later.",
            style = Ab.type.small, color = Ab.colors.muted, modifier = Modifier.padding(horizontal = 4.dp, vertical = 6.dp),
        )
    }
}
