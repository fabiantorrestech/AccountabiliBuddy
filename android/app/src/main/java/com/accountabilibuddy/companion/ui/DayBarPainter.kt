package com.accountabilibuddy.companion.ui

import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.Paint
import android.graphics.RectF
import com.accountabilibuddy.companion.engine.DayBarModel
import com.accountabilibuddy.companion.engine.DayBarSpan

/**
 * Draws the day bar like the Windows app's DayBarControl: events on top, the day plan's parts in their category
 * colours (with their names when there's room, outside compact), the actual lane (sleep Health Connect measured),
 * the night and lost sleep shaded, hour ticks and the red now line. One painter for the app, the widget and the
 * notification, so all three look the same.
 */
object DayBarPainter {
    /** Lane positions in dp. */
    private class Lanes(
        val eventHeight: Float, val plannedTop: Float, val plannedHeight: Float, val actualTop: Float, val actualHeight: Float,
        val axisTop: Float, val totalHeight: Float, val nowOverhang: Float, val textSize: Float,
    )

    private val normal = Lanes(4f, 7f, 16f, 26f, 8f, 38f, 54f, 3f, 11f)
    private val small = Lanes(3f, 5f, 10f, 17f, 5f, 24f, 36f, 2f, 9f)

    private const val FOCUS = 0xFF0F766E.toInt()
    private const val EVENT = 0xD964748B.toInt()
    private const val NOW = 0xFFDC2626.toInt()
    private const val NIGHT = 0x474F46E5
    private const val MISSED = 0x59DC2626
    private const val SLEPT = 0xFF6366F1.toInt()

    // Plan parts at 85% like on the PC; on the notification's teal card a light edge keeps teal parts visible.
    private const val PART_ALPHA = 0xD9000000.toInt()
    private const val PART_EDGE_ON_ACCENT = 0x80FFFFFF.toInt()
    private const val MIN_PART_WIDTH_FOR_NAME_DP = 28f
    private const val PART_TEXT_SP = 10f

    fun heightDp(compact: Boolean): Float = (if (compact) small else normal).totalHeight

    /** @param onAccent drawn on the notification's teal card, where teal blocks would vanish: they turn white or dark teal. */
    fun bitmap(model: DayBarModel, widthPx: Int, density: Float, dark: Boolean, compact: Boolean, onAccent: Boolean = false): Bitmap {
        val height = (heightDp(compact) * density).toInt().coerceAtLeast(1)
        val bitmap = Bitmap.createBitmap(widthPx.coerceAtLeast(1), height, Bitmap.Config.ARGB_8888)
        draw(Canvas(bitmap), model, widthPx.toFloat(), density, dark, compact, onAccent)
        return bitmap
    }

    fun draw(canvas: Canvas, model: DayBarModel, width: Float, density: Float, dark: Boolean, compact: Boolean, onAccent: Boolean = false) {
        val lanes = if (compact) small else normal
        fun dp(value: Float) = value * density
        val paint = Paint(Paint.ANTI_ALIAS_FLAG)
        val track = when {
            onAccent && dark -> 0x33FFFFFF
            onAccent -> 0x260F3B38
            dark -> 0x28FFFFFF
            else -> 0x1C000000
        }
        val text = if (dark) 0xBEFFFFFF.toInt() else 0xAA000000.toInt()
        val planned = when {
            onAccent && dark -> 0x99FFFFFF.toInt()
            onAccent -> 0x8C134E4A.toInt()
            else -> (FOCUS and 0x00FFFFFF) or 0x73000000
        }

        paint.color = track
        canvas.drawRoundRect(RectF(0f, dp(lanes.plannedTop), width, dp(lanes.plannedTop + lanes.plannedHeight)), dp(4f), dp(4f), paint)
        canvas.drawRoundRect(RectF(0f, dp(lanes.actualTop), width, dp(lanes.actualTop + lanes.actualHeight)), dp(3f), dp(3f), paint)

        val nightHeight = lanes.actualTop + lanes.actualHeight - lanes.plannedTop
        spans(canvas, paint, model.night, NIGHT, width, dp(lanes.plannedTop), dp(nightHeight), dp(3f))
        spans(canvas, paint, model.missed, MISSED, width, dp(lanes.plannedTop), dp(nightHeight), dp(3f))
        spans(canvas, paint, model.events, EVENT, width, 0f, dp(lanes.eventHeight), dp(1f))
        planSpans(canvas, paint, model.planned, planned, width, dp(lanes.plannedTop), dp(lanes.plannedHeight), dp(4f), onAccent)
        if (!compact) {
            partNames(canvas, model.planned, width, density, dp(lanes.plannedTop), dp(lanes.plannedHeight))
        }
        spans(canvas, paint, model.actual, SLEPT, width, dp(lanes.actualTop), dp(lanes.actualHeight), dp(3f))

        paint.color = track
        paint.strokeWidth = dp(1f)
        paint.textSize = dp(lanes.textSize)
        val textPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
            color = text
            textSize = dp(lanes.textSize)
        }
        var lastRight = -Float.MAX_VALUE
        for (tick in model.ticks) {
            val x = (tick.position * width).toFloat()
            canvas.drawLine(x, dp(lanes.actualTop + lanes.actualHeight), x, dp(lanes.axisTop), paint)
            val labelWidth = textPaint.measureText(tick.label)
            val left = (x - labelWidth / 2).coerceIn(0f, (width - labelWidth).coerceAtLeast(0f))
            if (left > lastRight + dp(4f)) {
                canvas.drawText(tick.label, left, dp(lanes.axisTop) + dp(lanes.textSize), textPaint)
                lastRight = left + labelWidth
            }
        }

        val nowX = (model.now.coerceIn(0.0, 1.0) * width).toFloat().coerceIn(dp(1f), width - dp(1f))
        paint.color = NOW
        paint.strokeWidth = dp(2f)
        canvas.drawLine(nowX, dp(lanes.plannedTop - lanes.nowOverhang), nowX, dp(lanes.actualTop + lanes.actualHeight + lanes.nowOverhang), paint)
    }

    /** Each part in its own colour (the lane colour when it has none). */
    private fun planSpans(
        canvas: Canvas, paint: Paint, spans: List<DayBarSpan>, fallback: Int, width: Float, top: Float, height: Float, radius: Float,
        onAccent: Boolean,
    ) {
        for (span in spans) {
            val left = (span.start.coerceIn(0.0, 1.0) * width).toFloat()
            val right = (span.end.coerceIn(0.0, 1.0) * width).toFloat()
            if (right - left < 0.5f) {
                continue
            }

            val rect = RectF(left, top, right, top + height)
            paint.style = Paint.Style.FILL
            paint.color = span.color?.let { (it and 0x00FFFFFF) or PART_ALPHA } ?: fallback
            canvas.drawRoundRect(rect, radius, radius, paint)
            if (onAccent && span.color != null) {
                paint.style = Paint.Style.STROKE
                paint.strokeWidth = 1f
                paint.color = PART_EDGE_ON_ACCENT
                canvas.drawRoundRect(rect, radius, radius, paint)
                paint.style = Paint.Style.FILL
            }
        }
    }

    /** Names inside parts at least 28 dp wide, cut with "…" to fit. */
    private fun partNames(canvas: Canvas, spans: List<DayBarSpan>, width: Float, density: Float, top: Float, height: Float) {
        val textPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
            color = 0xFFFFFFFF.toInt()
            textSize = PART_TEXT_SP * density
        }
        val padding = 3f * density
        for (span in spans) {
            val name = span.name ?: continue
            val left = (span.start.coerceIn(0.0, 1.0) * width).toFloat()
            val right = (span.end.coerceIn(0.0, 1.0) * width).toFloat()
            if (right - left < MIN_PART_WIDTH_FOR_NAME_DP * density) {
                continue
            }

            val room = right - left - 2 * padding
            var shown = name
            while (shown.isNotEmpty() && textPaint.measureText(if (shown == name) shown else "$shown…") > room) {
                shown = shown.dropLast(1)
            }
            if (shown.isEmpty()) {
                continue
            }

            val text = if (shown == name) shown else "$shown…"
            val baseline = top + (height - textPaint.descent() - textPaint.ascent()) / 2
            canvas.drawText(text, left + padding, baseline, textPaint)
        }
    }

    private fun spans(canvas: Canvas, paint: Paint, spans: List<DayBarSpan>, color: Int, width: Float, top: Float, height: Float, radius: Float) {
        paint.color = color
        for (span in spans) {
            val left = (span.start.coerceIn(0.0, 1.0) * width).toFloat()
            val right = (span.end.coerceIn(0.0, 1.0) * width).toFloat()
            if (right - left >= 0.5f) {
                canvas.drawRoundRect(RectF(left, top, right, top + height), radius, radius, paint)
            }
        }
    }
}
