package com.accountabilibuddy.companion.ui

import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.AccountBalanceWallet

import androidx.compose.material.icons.outlined.Computer
import androidx.compose.material.icons.outlined.Alarm
import androidx.compose.material.icons.outlined.Lightbulb
import androidx.compose.material.icons.outlined.Timer
import androidx.compose.material.icons.outlined.TrackChanges
import androidx.compose.material.icons.outlined.ViewAgenda
import androidx.compose.material.icons.outlined.DownloadForOffline
import androidx.compose.material.icons.outlined.EventBusy

import androidx.compose.material.icons.outlined.MusicNote
import androidx.compose.material.icons.outlined.PhoneAndroid
import androidx.compose.material.icons.outlined.Restaurant
import androidx.compose.material.icons.outlined.Science
import androidx.compose.material.icons.outlined.PlayCircle
import androidx.compose.material.icons.outlined.Checklist
import androidx.compose.material.icons.outlined.Shield
import androidx.compose.material.icons.outlined.Timeline
import androidx.compose.material.icons.outlined.Work
import androidx.compose.ui.graphics.vector.ImageVector
import com.accountabilibuddy.companion.today.TodayItem

/** The icon of an arrangeable part of Today, in the Today layout sheet (Material icons, ADR 32). */
fun TodayItem.icon(): ImageVector = when (this) {
    TodayItem.SessionCard -> Icons.Outlined.PlayCircle
    TodayItem.ThisSession -> Icons.Outlined.Checklist
    TodayItem.Guard -> Icons.Outlined.Shield
    TodayItem.LunchChip -> Icons.Outlined.Restaurant
    TodayItem.PcMissing -> Icons.Outlined.Computer
    TodayItem.Goals -> Icons.Outlined.TrackChanges
    TodayItem.NextUp -> Icons.Outlined.ViewAgenda
    TodayItem.Ring -> Icons.Outlined.Timer
    TodayItem.Comebacks -> Icons.Outlined.Lightbulb
    TodayItem.SmallCards -> Icons.Outlined.PhoneAndroid
    TodayItem.Noise -> Icons.Outlined.MusicNote
    TodayItem.DayBar -> Icons.Outlined.Timeline
    TodayItem.WorkUse -> Icons.Outlined.Work
    TodayItem.LunchCard -> Icons.Outlined.Restaurant
    TodayItem.Beeminder -> Icons.Outlined.AccountBalanceWallet
    TodayItem.Timers -> Icons.Outlined.Alarm
    TodayItem.DayOff -> Icons.Outlined.EventBusy
    TodayItem.Import -> Icons.Outlined.DownloadForOffline
    TodayItem.TestSession -> Icons.Outlined.Science
}
