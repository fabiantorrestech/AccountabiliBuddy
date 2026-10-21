package com.accountabilibuddy.companion.engine

import com.accountabilibuddy.companion.model.DayTemplate
import java.util.UUID
import kotlin.math.abs

/** What a drag on the day plan bar changes: a part's start, its end, or the whole part (moved, keeping its length). */
enum class BarGrabKind { Start, End, Whole }

/** One drawn stretch of a part on the 24-hour bar, in minutes; a part crossing midnight is two (evening and morning). */
data class BarPiece(
    val partId: UUID,
    val start: Int,
    val end: Int,
    val startIsEdge: Boolean,
    val endIsEdge: Boolean,
    val crossesMidnight: Boolean,
)

/** Which part, and which bit of it, a finger grabs on the phone's day plan bar (ADR 19). */
object DayBarGrab {
    private const val MINUTES_PER_DAY = 24 * 60

    fun pieces(template: DayTemplate): List<BarPiece> = template.parts.flatMap { part ->
        DayTemplateEditing.ranges(part).map { (start, end) ->
            BarPiece(
                part.id, start, end,
                startIsEdge = !part.crossesMidnight || start > 0,
                endIsEdge = !part.crossesMidnight || end < MINUTES_PER_DAY,
                crossesMidnight = part.crossesMidnight,
            )
        }
    }

    /**
     * The part under the finger first: its edges count within [edgeGrabPx] or a third of its width (whichever is
     * smaller), so a short part can still be moved by its middle. Only on empty space does the nearest edge within
     * [edgeGrabPx] count. A part crossing midnight can't be moved whole. Null when nothing is grabbed.
     */
    fun pick(pieces: List<BarPiece>, x: Float, width: Float, edgeGrabPx: Float): Pair<BarPiece, BarGrabKind>? {
        fun xOf(minute: Int) = minute.toFloat() / MINUTES_PER_DAY * width

        val under = pieces.firstOrNull { x >= xOf(it.start) && x <= xOf(it.end) }
        if (under != null) {
            val grab = minOf(edgeGrabPx, (xOf(under.end) - xOf(under.start)) / 3)
            val kind = when {
                under.startIsEdge && x - xOf(under.start) <= grab -> BarGrabKind.Start
                under.endIsEdge && xOf(under.end) - x <= grab -> BarGrabKind.End
                under.crossesMidnight -> null
                else -> BarGrabKind.Whole
            }
            return kind?.let { under to it }
        }

        val edges = pieces.flatMap { piece ->
            listOfNotNull(
                if (piece.startIsEdge) Triple(piece, BarGrabKind.Start, abs(x - xOf(piece.start))) else null,
                if (piece.endIsEdge) Triple(piece, BarGrabKind.End, abs(x - xOf(piece.end))) else null,
            )
        }
        return edges.filter { it.third <= edgeGrabPx }.minByOrNull { it.third }?.let { it.first to it.second }
    }
}
