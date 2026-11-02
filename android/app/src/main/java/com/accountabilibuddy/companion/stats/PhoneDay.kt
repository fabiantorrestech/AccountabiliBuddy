package com.accountabilibuddy.companion.stats

import java.time.Duration
import java.time.LocalDate

/**
 * One day of this phone's own use, read from Android's usage records on the phone (never synced).
 *
 * @property total Time an app was in front.
 * @property inFocus The part of [total] inside counted focus (a PC session, or a session on another device).
 * @property pickups Times the phone was unlocked; null without usage access or on Android before 9.
 */
data class PhoneDay(val date: LocalDate, val total: Duration, val inFocus: Duration, val pickups: Int?)
