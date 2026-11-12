package com.accountabilibuddy.companion.ui.stats

import com.accountabilibuddy.companion.engine.NightRecord
import com.accountabilibuddy.companion.model.LocalWindow
import com.accountabilibuddy.companion.stats.PhoneDay
import com.accountabilibuddy.companion.stats.StatsData
import com.accountabilibuddy.companion.usage.PhoneUsage
import java.time.Duration
import java.time.LocalDate

/**
 * Everything the Stats pane draws from, handed in by the host: the focus numbers from the received sync files, and this phone's
 * own sleep and phone use (local, never synced). The pane works its views out from this and changes nothing.
 *
 * @property hasDevices The phone has received a file from some device: with none, the empty states ask for a link first.
 * @property phoneDays This phone's last days of use (about a week); empty without Usage access.
 * @property phoneToday Today's use by app; null without Usage access.
 * @property nights The nights Health Connect measured, with the smart bedtime's bedtimes asked where it ran.
 * @property sleepGoal The sleep goal (Settings › Sleep).
 * @property goals This phone's goals for the Goals view (round 2 R6b4); null until that view is opened.
 * @property sleepOn The planned nights touching a date (empty with sleep mode off), for the day strip.
 */
class StatsPaneModel(
    val data: StatsData,
    val hasDevices: Boolean,
    val phoneDays: List<PhoneDay>,
    val phoneToday: PhoneUsage?,
    val nights: List<NightRecord>,
    val sleepGoal: Duration,
    val sleepNight: SleepNightModel,
    val goals: StatsGoalsModel? = null,
    val sleepOn: (LocalDate) -> List<LocalWindow>,
)
