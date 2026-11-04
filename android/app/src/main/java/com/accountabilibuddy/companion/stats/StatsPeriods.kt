package com.accountabilibuddy.companion.stats

import java.time.LocalDate

/**
 * Which period a Stats view shows. The position stepped to with the arrows is held by the pane for the visit only, never in
 * the preferences (they keep the kind of range alone): `null` there means "the current period", which moves on by itself when
 * a new day comes. A Custom range is the exception: it stays as picked and is only held inside the days the phone keeps.
 */
object StatsPeriods {
    /** The period of [kind] to show, given the [stepped] position held (null for none). Day is the pane's own, not resolved here. */
    fun resolve(kind: StatsRange, stepped: StatsPeriod?, today: LocalDate, firstDay: LocalDate): StatsPeriod = when {
        stepped == null || stepped.kind != kind -> StatsPeriod.current(kind, today)
        kind == StatsRange.Custom -> clamp(stepped, firstDay, today)
        else -> stepped
    }

    /** What to hold after moving to [period]: null when it is the current one, so it keeps following the date. */
    fun toHold(period: StatsPeriod, today: LocalDate): StatsPeriod? =
        if (period.kind != StatsRange.Custom && period == StatsPeriod.current(period.kind, today)) null else period

    /** A custom range held inside the days the phone keeps (the pickers' limits); the two dates come out in order. */
    fun clamp(period: StatsPeriod, firstDay: LocalDate, today: LocalDate): StatsPeriod =
        StatsPeriod.custom(clampDate(period.from, firstDay, today), clampDate(period.to, firstDay, today))

    fun clampDate(date: LocalDate, first: LocalDate, last: LocalDate): LocalDate = if (date < first) first else if (date > last) last else date
}
