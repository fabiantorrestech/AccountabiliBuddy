package com.accountabilibuddy.companion.ui.stats

import com.accountabilibuddy.companion.engine.NightRecord
import com.accountabilibuddy.companion.model.LocalWindow
import com.accountabilibuddy.companion.stats.PhoneDay
import com.accountabilibuddy.companion.stats.StatsData
import com.accountabilibuddy.companion.ui.HealthStatus
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
    val sleepOn: (LocalDate) -> List<LocalWindow>,
)

/**
 * What the Sleep view says about the night, the content the old Stats card held: the planned night, and whether the watch's
 * measured sleep can be read from Health Connect.
 *
 * @property plannedText "Planned: 10:30 PM – 6:30 AM (8h 00m)"; null with sleep mode off.
 * @property lastSleepText "Last sleep: 7h 05m (11:20 PM – 6:25 AM)"; null when none was recorded.
 */
class SleepNightModel(val plannedText: String?, val health: HealthStatus, val lastSleepText: String?)
