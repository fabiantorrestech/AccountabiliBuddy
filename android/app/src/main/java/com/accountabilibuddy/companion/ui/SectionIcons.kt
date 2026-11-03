package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.size
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Bedtime
import androidx.compose.material.icons.outlined.Backup
import androidx.compose.material.icons.outlined.Block
import androidx.compose.material.icons.outlined.Timer
import androidx.compose.material.icons.outlined.Apps
import androidx.compose.material.icons.outlined.Checklist
import androidx.compose.material.icons.outlined.Notifications
import androidx.compose.material.icons.outlined.NotificationsActive
import androidx.compose.material.icons.outlined.Alarm
import androidx.compose.material.icons.outlined.CalendarToday
import androidx.compose.material.icons.outlined.CalendarViewDay
import androidx.compose.material.icons.outlined.Build
import androidx.compose.material.icons.outlined.Extension
import androidx.compose.material.icons.outlined.Info
import androidx.compose.material.icons.outlined.Insights
import androidx.compose.material.icons.outlined.Lock
import androidx.compose.material.icons.outlined.Settings
import androidx.compose.material.icons.outlined.Shield
import androidx.compose.material.icons.outlined.Storage
import androidx.compose.material.icons.outlined.WbSunny
import androidx.compose.material.icons.outlined.Computer
import androidx.compose.material.icons.outlined.Devices
import androidx.compose.material.icons.outlined.EventNote
import androidx.compose.material.icons.outlined.Flag
import androidx.compose.material.icons.outlined.EmojiNature
import androidx.compose.material.icons.outlined.Headphones
import androidx.compose.material.icons.outlined.Inventory2
import androidx.compose.material.icons.outlined.Link
import androidx.compose.material.icons.outlined.MusicNote
import androidx.compose.material.icons.outlined.Palette
import androidx.compose.material.icons.outlined.PhoneAndroid
import androidx.compose.material.icons.outlined.PowerSettingsNew
import androidx.compose.material.icons.outlined.Restaurant
import androidx.compose.material.icons.outlined.DirectionsWalk
import androidx.compose.material.icons.outlined.Restore
import androidx.compose.material.icons.outlined.Security
import androidx.compose.material.icons.outlined.Share
import androidx.compose.material.icons.outlined.Smartphone
import androidx.compose.material.icons.outlined.TaskAlt
import androidx.compose.material.icons.outlined.Tune
import androidx.compose.material.icons.outlined.Watch
import androidx.compose.material.icons.outlined.BeachAccess
import androidx.compose.material.icons.outlined.Science
import androidx.compose.material3.Icon
import androidx.compose.material3.LocalContentColor
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp

/**
 * The icon for each section and card title (ADR 32), the same meaning as on the PC: e.g. a moon for Sleep, headphones
 * for the headphones switch. Titles that start with a known word ("PC · DESK", "Tasks · …") match it.
 */
object SectionIcons {
    /*
     * The same meaning gets the same icon on the PC and the phone (ADR 32). PC: Material.Icons.Avalonia kinds; phone: the
     * material-icons-extended Outlined set. Used by the 5-tab bar and the Settings rows of the redesign.
     *
     *   Page              PC kind                   Phone (Outlined)
     *   Today             CalendarToday             CalendarToday
     *   Tasks             FormatListChecks          Checklist
     *   Plan              ViewAgendaOutline         CalendarViewDay
     *   Stats             ChartTimelineVariant      Insights
     *   Settings          Cog                       Settings
     *   Your day          WeatherSunny              WbSunny
     *   Sleep             WeatherNight              Bedtime
     *   Focus sessions    TimerOutline              Timer
     *   Guards            ShieldOutline             Shield
     *   Alerts & on-screen BellOutline              Notifications
     *   Devices & sync    Devices                   Devices
     *   Connections       PuzzleOutline             Extension
     *   Appearance        PaletteOutline            Palette
     *   App & data        DatabaseOutline           Storage
     *   Setup             Wrench                    Build
     *   About             InformationOutline        Info
     *   Resting (locked)  LockOutline               Lock
     *   Daily steps       Walk                      DirectionsWalk
     */
    val Today: ImageVector = Icons.Outlined.CalendarToday
    val Tasks: ImageVector = Icons.Outlined.Checklist
    val Plan: ImageVector = Icons.Outlined.CalendarViewDay
    val Stats: ImageVector = Icons.Outlined.Insights
    val Settings: ImageVector = Icons.Outlined.Settings
    val YourDay: ImageVector = Icons.Outlined.WbSunny
    val Sleep: ImageVector = Icons.Outlined.Bedtime
    val FocusSessions: ImageVector = Icons.Outlined.Timer
    val Guards: ImageVector = Icons.Outlined.Shield
    val AlertsAndOnScreen: ImageVector = Icons.Outlined.Notifications
    val DevicesAndSync: ImageVector = Icons.Outlined.Devices
    val Connections: ImageVector = Icons.Outlined.Extension
    val Appearance: ImageVector = Icons.Outlined.Palette
    val AppAndData: ImageVector = Icons.Outlined.Storage
    val Setup: ImageVector = Icons.Outlined.Build
    val About: ImageVector = Icons.Outlined.Info
    val Resting: ImageVector = Icons.Outlined.Lock

    private val byTitle: List<Pair<String, ImageVector>> = listOf(
        "AccountabiliBuddy on this phone" to Icons.Outlined.PowerSettingsNew,
        "Permissions" to Icons.Outlined.Security,
        "Profiles from the PC" to Icons.Outlined.Inventory2,
        "On the phone" to Icons.Outlined.Tune,
        "Appearance" to Icons.Outlined.Palette,
        "Startup" to Icons.Outlined.PowerSettingsNew,
        "Sleep" to Icons.Outlined.Bedtime,
        "Smart bedtime" to Icons.Outlined.Bedtime,
        "Wake-up alarm" to Icons.Outlined.Alarm,
        "Bedtime reminder" to Icons.Outlined.NotificationsActive,
        "Timeline notification" to Icons.Outlined.Notifications,
        "Setup" to Icons.Outlined.Checklist,
        "Essentials" to Icons.Outlined.Apps,
        "Emergency phone time" to Icons.Outlined.Timer,
        "Backup" to Icons.Outlined.Backup,
        "Noise player" to Icons.Outlined.MusicNote,
        "Play when headphones connect" to Icons.Outlined.Headphones,
        "Curbox" to Icons.Outlined.PhoneAndroid,
        "Other blockers" to Icons.Outlined.Block,
        "Pixel Watch" to Icons.Outlined.Watch,
        "PC" to Icons.Outlined.Computer,
        "Your devices" to Icons.Outlined.Devices,
        "Link this phone" to Icons.Outlined.Link,
        "Restore from an older device" to Icons.Outlined.Restore,
        "How this phone shares" to Icons.Outlined.Share,
        "Bring your PC's profiles" to Icons.Outlined.Inventory2,
        "Today" to Icons.Outlined.CalendarToday,
        "Tomorrow" to Icons.Outlined.EventNote,
        "Phone today" to Icons.Outlined.Smartphone,
        "Come back to" to Icons.Outlined.Flag,
        "Next session" to Icons.Outlined.Checklist,
        "Beeminder" to Icons.Outlined.EmojiNature,
        "Tasks" to Icons.Outlined.TaskAlt,
        "Lunch" to Icons.Outlined.Restaurant,
        "Daily steps" to Icons.Outlined.DirectionsWalk,
        "Timers" to Icons.Outlined.Timer,
        "Alarms" to Icons.Outlined.Alarm,
        "Not working today" to Icons.Outlined.BeachAccess,
        "More options" to Icons.Outlined.Tune,
        "Test session" to Icons.Outlined.Science,
    )

    fun of(title: String): ImageVector? =
        byTitle.firstOrNull { (key, _) -> title == key || title.startsWith("$key ") || title.startsWith("$key·") }?.second
}

/** A title with its section icon in front (none when the title has no icon). */
@Composable
fun IconTitle(text: String, style: TextStyle, icon: ImageVector? = SectionIcons.of(text), fontWeight: FontWeight? = null, modifier: Modifier = Modifier) {
    Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp), modifier = modifier) {
        if (icon != null) {
            Icon(icon, contentDescription = null, tint = LocalContentColor.current.copy(alpha = 0.8f), modifier = Modifier.size(20.dp))
        }
        Text(text, style = style, fontWeight = fontWeight)
    }
}
