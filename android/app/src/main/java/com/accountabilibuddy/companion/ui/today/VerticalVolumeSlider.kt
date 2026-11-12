package com.accountabilibuddy.companion.ui.today

import androidx.compose.foundation.background
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.gestures.detectVerticalDragGestures
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.semantics.ProgressBarRangeInfo
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.progressBarRangeInfo
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.setProgress
import androidx.compose.ui.semantics.stateDescription
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.ui.theme.Ab
import kotlin.math.roundToInt

/**
 * A vertical 0–100 slider (the noise bar's volume pop-up, CN §3): drag or tap along the track; the top is 100. [onChange] gets each
 * new value while the finger moves; [onDone] when it lifts, so the player is told once. A screen reader reads and sets the value as
 * a progress control ("Noise volume, 40 percent").
 */
@Composable
fun VerticalVolumeSlider(value: Int, onChange: (Int) -> Unit, onDone: () -> Unit, modifier: Modifier = Modifier, trackHeight: Dp = 138.dp) {
    val colors = Ab.colors
    val current by rememberUpdatedState(value)
    val setValue by rememberUpdatedState(onChange)
    val done by rememberUpdatedState(onDone)
    val thumb = 24.dp
    val thumbPx = with(LocalDensity.current) { thumb.toPx() }
    BoxWithConstraints(
        modifier.width(48.dp).height(trackHeight)
            .semantics {
                contentDescription = "Noise volume"
                stateDescription = "$value percent"
                progressBarRangeInfo = ProgressBarRangeInfo(value.toFloat(), 0f..100f, steps = 99)
                setProgress { target ->
                    setValue(target.roundToInt().coerceIn(0, 100))
                    done()
                    true
                }
            },
        contentAlignment = Alignment.BottomCenter,
    ) {
        val heightPx = constraints.maxHeight.toFloat()
        fun valueAt(y: Float): Int {
            val usable = (heightPx - thumbPx).coerceAtLeast(1f)
            return (100f * (1f - ((y - thumbPx / 2) / usable))).roundToInt().coerceIn(0, 100)
        }
        Box(
            Modifier.fillMaxWidth().fillMaxHeight()
                .pointerInput(Unit) {
                    detectTapGestures { offset ->
                        setValue(valueAt(offset.y))
                        done()
                    }
                }
                .pointerInput(Unit) {
                    detectVerticalDragGestures(
                        onDragEnd = { done() },
                        onDragCancel = { done() },
                    ) { pointer, _ ->
                        pointer.consume()
                        setValue(valueAt(pointer.position.y))
                    }
                },
        )
        val fraction = current / 100f
        val trackDp = trackHeight - thumb
        // The track, its filled part from the bottom, and the thumb.
        Box(Modifier.align(Alignment.Center).width(8.dp).height(trackDp).background(colors.bg, CircleShape))
        Box(Modifier.offset(y = -thumb / 2).width(8.dp).height(trackDp * fraction).background(colors.accent, CircleShape))
        Box(Modifier.offset(y = -(trackDp * fraction)).size(thumb).background(colors.ink, CircleShape))
    }
}
