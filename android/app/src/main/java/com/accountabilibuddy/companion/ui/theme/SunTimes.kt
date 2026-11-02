package com.accountabilibuddy.companion.ui.theme

import java.time.Instant
import java.time.LocalDate
import java.time.ZoneId
import java.time.ZoneOffset
import java.time.ZonedDateTime
import kotlin.math.PI
import kotlin.math.acos
import kotlin.math.asin
import kotlin.math.cos
import kotlin.math.pow
import kotlin.math.sin
import kotlin.math.tan

/**
 * Sunrise and sunset from the NOAA solar calculator formulas (accurate to about a minute away from the poles): a port of
 * the PC's `SunTimes` (ADR 49), so both give the same moments. Pure: a date, a place and a time zone in, moments out.
 * Nothing here looks anything up.
 */
object SunTimes {
    // The sun's centre 90.833 degrees from straight up: its edge on the horizon, plus atmospheric refraction.
    private const val SUNRISE_ZENITH_DEGREES = 90.833

    /** The day's sunrise and sunset, or null when the sun does not rise or set that day (polar day or night). */
    fun calculate(date: LocalDate, point: GeoPoint, zone: ZoneId): SunTimesResult? {
        if (!point.isValid) return null

        val midnightUtc = date.atStartOfDay(ZoneOffset.UTC).toInstant()
        val latitude = toRadians(point.latitude)

        // Solar noon in minutes after 00:00 UTC of that date. The sun's position at noon decides the day's length, so work
        // out the position at the estimated noon, correct the noon with it, and repeat once.
        var noonMinutes = 720 - 4 * point.longitude
        var position = positionAt(midnightUtc.plusSeconds((noonMinutes * 60).toLong()))
        noonMinutes = 720 - 4 * point.longitude - position.equationOfTimeMinutes
        position = positionAt(midnightUtc.plusSeconds((noonMinutes * 60).toLong()))
        noonMinutes = 720 - 4 * point.longitude - position.equationOfTimeMinutes

        val cosHourAngle = cos(toRadians(SUNRISE_ZENITH_DEGREES)) / (cos(latitude) * cos(position.declination)) -
            tan(latitude) * tan(position.declination)
        if (cosHourAngle.isNaN() || cosHourAngle > 1 || cosHourAngle < -1) return null

        val hourAngleDegrees = toDegrees(acos(cosHourAngle))
        return SunTimesResult(
            toLocal(midnightUtc.plusSeconds(((noonMinutes - 4 * hourAngleDegrees) * 60).toLong()), zone),
            toLocal(midnightUtc.plusSeconds(((noonMinutes + 4 * hourAngleDegrees) * 60).toLong()), zone),
        )
    }

    private fun positionAt(utc: Instant): SolarPosition {
        val julianDay = utc.epochSecond / 86400.0 + utc.nano / 86400e9 + 2440587.5
        val t = (julianDay - 2451545.0) / 36525.0

        val meanLongitude = normalize(280.46646 + t * (36000.76983 + t * 0.0003032), 360.0)
        val meanAnomaly = 357.52911 + t * (35999.05029 - 0.0001537 * t)
        val eccentricity = 0.016708634 - t * (0.000042037 + 0.0000001267 * t)
        val meanAnomalyRadians = toRadians(meanAnomaly)

        val equationOfCentre = sin(meanAnomalyRadians) * (1.914602 - t * (0.004817 + 0.000014 * t)) +
            sin(2 * meanAnomalyRadians) * (0.019993 - 0.000101 * t) +
            sin(3 * meanAnomalyRadians) * 0.000289
        val trueLongitude = meanLongitude + equationOfCentre

        val omega = 125.04 - 1934.136 * t
        val apparentLongitude = trueLongitude - 0.00569 - 0.00478 * sin(toRadians(omega))

        val meanObliquity = 23 + (26 + (21.448 - t * (46.815 + t * (0.00059 - t * 0.001813))) / 60) / 60
        val obliquity = toRadians(meanObliquity + 0.00256 * cos(toRadians(omega)))

        val declination = asin(sin(obliquity) * sin(toRadians(apparentLongitude)))

        val y = tan(obliquity / 2).pow(2)
        val meanLongitudeRadians = toRadians(meanLongitude)
        val equationOfTime = y * sin(2 * meanLongitudeRadians) -
            2 * eccentricity * sin(meanAnomalyRadians) +
            4 * eccentricity * y * sin(meanAnomalyRadians) * cos(2 * meanLongitudeRadians) -
            0.5 * y * y * sin(4 * meanLongitudeRadians) -
            1.25 * eccentricity * eccentricity * sin(2 * meanAnomalyRadians)

        return SolarPosition(declination, 4 * toDegrees(equationOfTime))
    }

    private fun toLocal(utc: Instant, zone: ZoneId): ZonedDateTime = utc.atZone(zone)

    private fun normalize(value: Double, modulus: Double): Double {
        val result = value % modulus
        return if (result < 0) result + modulus else result
    }

    private fun toRadians(degrees: Double) = degrees * PI / 180

    private fun toDegrees(radians: Double) = radians * 180 / PI

    private class SolarPosition(val declination: Double, val equationOfTimeMinutes: Double)
}
