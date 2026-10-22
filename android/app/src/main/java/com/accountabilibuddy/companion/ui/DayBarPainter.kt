package com.accountabilibuddy.companion.ui

import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.Paint
import android.graphics.RectF
import com.accountabilibuddy.companion.engine.DayBarBuilder
import com.accountabilibuddy.companion.engine.DayBarModel
import com.accountabilibuddy.companion.engine.DayBarSpan
import com.accountabilibuddy.companion.model.BarRow

/**
 * Draws the day bar in the Windows app's rows (the on-screen timeline's, without labels): Calendar (events in their
 * calendar's colour), Plan (the day plan's parts in their category colours, with names when there's room outside
 * compact) and Focus (the devices' sessions with red drift marks), in the PC's order, then a thin strip of the sleep
 * Health Connect measured; the night and lost sleep shaded behind them, hour ticks and the red now line. One painter
 * for the app, the widget and the notification, so all three look the same.
 */
object DayBarPainter {
    /** Row heights, gaps and the rest in dp. */
    private class Sizes(
        val calendar: Float, val plan: Float, val focus: Float, val slept: Float, val gap: Float,
        val totalHeight: Float, val nowOverhang: Float, val nowWidth: Float, val textSize: Float,
    ) {
        fun of(row: BarRow): Float = when (row) {
            BarRow.Calendar -> calendar
            BarRow.Plan -> plan
            BarRow.Focus -> focus
            BarRow.Blockers -> 0f
        }
    }

    private val normal = Sizes(5f, 13f, 8f, 3f, 2f, 54f, 3f, 3f, 11f)
    private val small = Sizes(3f, 8f, 5f, 2f, 1f, 36f, 2f, 2.5f, 9f)

    private const val FOCUS = 0xFF0F766E.toInt()
    private const val EVENT = 0xD964748B.toInt()
    private const val NOW = 0xFFDC2626.toInt()
    private const val NIGHT = 0x474F46E5
    private const val MISSED = 0x59DC2626
    private const val SLEPT = 0xFF6366F1.toInt()
    private const val DRIFT = 0xFFEF4444.toInt()
    private const val NOT_IN_SESSION = DayBarBuilder.NOT_IN_SESSION_COLOR
    private const val MIN_DRIFT_PX = 2f

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
        val sizes = if (compact) small else normal
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
        // On the notification's teal card, teal sessions would vanish: white on the dark card, dark teal on the light one.
        val focused = when {
            onAccent && dark -> 0xE6FFFFFF.toInt()
            onAccent -> 0xFF134E4A.toInt()
            else -> FOCUS
        }

        // Rows top to bottom in the PC's order, then the slept strip; each gets its track.
        var cursor = 0f
        val tops = mutableMapOf<BarRow, Float>()
        for (row in model.rows) {
            tops[row] = cursor
            cursor += sizes.of(row) + sizes.gap
        }

        val sleptTop = cursor
        val bottom = sleptTop + sizes.slept
        val axisTop = bottom + sizes.gap * 2
        paint.color = track
        for ((row, top) in tops) {
            val radius = if (row == BarRow.Plan) dp(4f) else dp(2f)
            canvas.drawRoundRect(RectF(0f, dp(top), width, dp(top + sizes.of(row))), radius, radius, paint)
        }
        canvas.drawRoundRect(RectF(0f, dp(sleptTop), width, dp(bottom)), dp(1.5f), dp(1.5f), paint)

        // The night and lost sleep behind every row, as on the PC.
        spans(canvas, paint, model.night, NIGHT, width, 0f, dp(bottom), dp(3f))
        spans(canvas, paint, model.missed, MISSED, width, 0f, dp(bottom), dp(3f))

        tops[BarRow.Calendar]?.let { top ->
            for (event in model.events) {
                spans(canvas, paint, listOf(event), event.color ?: EVENT, width, dp(top), dp(sizes.calendar), dp(1f))
            }
        }
        tops[BarRow.Plan]?.let { top ->
            planSpans(canvas, paint, model.planned, planned, width, dp(top), dp(sizes.plan), dp(4f), onAccent)
            if (!compact) {
                partNames(canvas, model.planned, width, density, dp(top), dp(sizes.plan))
            }
        }
        tops[BarRow.Focus]?.let { top ->
            // Work time with no session under the sessions, in slate.
            spans(canvas, paint, model.gaps, NOT_IN_SESSION, width, dp(top), dp(sizes.focus), dp(2f))
            spans(canvas, paint, model.focus, focused, width, dp(top), dp(sizes.focus), dp(2f))
            driftMarks(canvas, paint, model.drift, width, dp(top), dp(sizes.focus))
        }
        spans(canvas, paint, model.actual, SLEPT, width, dp(sleptTop), dp(sizes.slept), dp(1.5f))

        paint.color = track
        paint.strokeWidth = dp(1f)
        paint.textSize = dp(sizes.textSize)
        val textPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
            color = text
            textSize = dp(sizes.textSize)
        }
        var lastRight = -Float.MAX_VALUE
        for (tick in model.ticks) {
            val x = (tick.position * width).toFloat()
            canvas.drawLine(x, dp(bottom), x, dp(axisTop), paint)
            val labelWidth = textPaint.measureText(tick.label)
            val left = (x - labelWidth / 2).coerceIn(0f, (width - labelWidth).coerceAtLeast(0f))
            if (left > lastRight + dp(4f)) {
                canvas.drawText(tick.label, left, dp(axisTop) + dp(sizes.textSize), textPaint)
                lastRight = left + labelWidth
            }
        }

        val half = dp(sizes.nowWidth) / 2
        val nowX = (model.now.coerceIn(0.0, 1.0) * width).toFloat().coerceIn(half, width - half)
        paint.color = NOW
        paint.strokeWidth = dp(sizes.nowWidth)
        canvas.drawLine(nowX, 0f, nowX, dp(bottom + sizes.nowOverhang), paint)
    }

    /** Red drift marks on the Focus row, at least 2 px wide so a short one still shows. */
    private fun driftMarks(canvas: Canvas, paint: Paint, spans: List<DayBarSpan>, width: Float, top: Float, height: Float) {
        paint.color = DRIFT
        for (span in spans) {
            val left = (span.start.coerceIn(0.0, 1.0) * width).toFloat()
            val right = maxOf((span.end.coerceIn(0.0, 1.0) * width).toFloat(), left + MIN_DRIFT_PX)
            canvas.drawRect(RectF(left, top, right, top + height), paint)
        }
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
