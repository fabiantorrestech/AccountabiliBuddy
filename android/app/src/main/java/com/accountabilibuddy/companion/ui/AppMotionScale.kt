package com.accountabilibuddy.companion.ui

import android.content.Context
import android.database.ContentObserver
import android.os.Handler
import android.os.Looper
import android.provider.Settings
import android.view.ViewGroup
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.runtime.Composable
import androidx.compose.ui.MotionDurationScale
import androidx.compose.ui.platform.compositionContext
import androidx.compose.ui.platform.createLifecycleAwareWindowRecomposer

/**
 * The speed every Compose animation in the app runs at (Settings › Appearance › Animations). Off makes each one finish at once:
 * the app's own fades and slides, and also Material's sheets, switches, ripples, list item moves and animated scrolls, which
 * the per-call [motionSpec] can't reach. Otherwise it follows the phone's own animator scale, as Compose does by default, so the
 * phone's "Remove animations" still applies. Fast and Slow stay with [motionSpec] (scaling here too would scale twice).
 * Flings are not touched (Compose keeps them at full speed on purpose); [stepScroll] handles them for e-ink.
 */
object AppMotionScale : MotionDurationScale {
    @Volatile
    private var systemScale = 1f
    private var watching = false

    override val scaleFactor: Float
        get() = if (UiPrefs.motion.value == MotionSpeed.Off) 0f else systemScale

    /** Reads the phone's animator scale now and whenever it changes; the first screen to open starts it, once. */
    fun watchSystemScale(context: Context) {
        if (watching) return
        watching = true
        val app = context.applicationContext
        systemScale = read(app)
        val observer = object : ContentObserver(Handler(Looper.getMainLooper())) {
            override fun onChange(selfChange: Boolean) {
                systemScale = read(app)
            }
        }
        app.contentResolver.registerContentObserver(Settings.Global.getUriFor(Settings.Global.ANIMATOR_DURATION_SCALE), false, observer)
    }

    private fun read(context: Context): Float =
        runCatching { Settings.Global.getFloat(context.contentResolver, Settings.Global.ANIMATOR_DURATION_SCALE, 1f) }
            .getOrDefault(1f)
}

/**
 * [setContent] with the app's own animation speed ([AppMotionScale]) in place of the phone's: the screen's composition, and the
 * dialogs and sheets opened from it, run their animations at that scale.
 */
fun ComponentActivity.setAbContent(content: @Composable () -> Unit) {
    AppMotionScale.watchSystemScale(this)
    findViewById<ViewGroup>(android.R.id.content).compositionContext =
        window.decorView.createLifecycleAwareWindowRecomposer(AppMotionScale, lifecycle)
    setContent(content = content)
}

/**
 * Inside a dialog or sheet: with animations off, its window closes without the system's fade. (Its opening fade, when the
 * phone has one, starts before the content is drawn; the phone's own "Remove animations" takes that away too.)
 */
@Composable
fun NoWindowAnimationWhenOff() {
    val off = LocalMotionScale.current <= 0f
    val view = androidx.compose.ui.platform.LocalView.current
    androidx.compose.runtime.SideEffect {
        if (off) (view.parent as? androidx.compose.ui.window.DialogWindowProvider)?.window?.setWindowAnimations(0)
    }
}
