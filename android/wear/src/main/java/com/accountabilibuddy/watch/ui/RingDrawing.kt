package com.accountabilibuddy.watch.ui

import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.drawscope.DrawScope
import androidx.compose.ui.graphics.drawscope.Stroke
import java.time.LocalTime
import kotlin.math.cos
import kotlin.math.sin

private const val MINUTES_PER_DAY = 24 * 60

/** Minutes after midnight; an end of 00:00 is the end of the day. */
fun minuteOf(time: LocalTime, isEnd: Boolean = false): Int {
    val minute = time.toSecondOfDay() / 60
    return if (isEnd && minute == 0) MINUTES_PER_DAY else minute
}

/** The angle of a time on the 24-hour dial: midnight at the top, noon at the bottom, clockwise. */
fun angleOf(minute: Int): Float = -90f + minute.toFloat() / MINUTES_PER_DAY * 360f

/** A ring's full circle in a dim colour, under its arcs. */
fun DrawScope.ringTrack(radius: Float, width: Float, color: Color) {
    drawCircle(color, radius, center, style = Stroke(width))
}

/** An arc on a ring from one time to another (across midnight when the end is earlier), with a hair gap at each end. */
fun DrawScope.timeArc(start: LocalTime, end: LocalTime, radius: Float, width: Float, color: Color) {
    val from = minuteOf(start)
    val to = minuteOf(end, isEnd = true)
    val minutes = if (to > from) to - from else to + MINUTES_PER_DAY - from
    val sweep = minutes.toFloat() / MINUTES_PER_DAY * 360f
    val gap = if (sweep > 4f) 0.8f else 0f
    drawArc(
        color = color,
        startAngle = angleOf(from) + gap,
        sweepAngle = sweep - 2 * gap,
        useCenter = false,
        topLeft = Offset(center.x - radius, center.y - radius),
        size = Size(radius * 2, radius * 2),
        style = Stroke(width, cap = StrokeCap.Butt),
    )
}

/** A ring filled from the top, clockwise, to [fraction] (0–1). */
fun DrawScope.progressArc(fraction: Float, radius: Float, width: Float, color: Color, at: Offset = center) {
    drawArc(
        color = color,
        startAngle = -90f,
        sweepAngle = 360f * fraction.coerceIn(0f, 1f),
        useCenter = false,
        topLeft = Offset(at.x - radius, at.y - radius),
        size = Size(radius * 2, radius * 2),
        style = Stroke(width, cap = StrokeCap.Round),
    )
}

/** The point on a circle of [radius] around the centre at a time of day. */
fun DrawScope.pointAt(minute: Int, radius: Float): Offset {
    val radians = Math.toRadians(angleOf(minute).toDouble())
    return Offset(center.x + radius * cos(radians).toFloat(), center.y + radius * sin(radians).toFloat())
}
