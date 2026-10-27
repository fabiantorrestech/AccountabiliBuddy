package com.accountabilibuddy.companion.placement

import android.content.Context
import android.hardware.Sensor
import android.hardware.SensorEvent
import android.hardware.SensorEventListener
import android.hardware.SensorManager
import android.os.Handler
import android.os.SystemClock

/**
 * One short burst of the accelerometer and the light sensor: registered, read for [BURST_MS], unregistered. Nothing
 * stays registered between bursts. A burst with too few accelerometer samples (the CPU slept through it, Doze) gives
 * null, never a guess. Runs on [handler]'s thread.
 */
class PlacementSampler(context: Context, private val handler: Handler) {
    /** The mean gravity and the movement over one burst, and the light level in it (null when none arrived). */
    data class Sample(val x: Float, val y: Float, val z: Float, val variance: Float, val lux: Float?)

    private val sensors = context.getSystemService(SensorManager::class.java)
    private val accelerometer: Sensor? = sensors.getDefaultSensor(Sensor.TYPE_ACCELEROMETER)
    private val light: Sensor? = sensors.getDefaultSensor(Sensor.TYPE_LIGHT)

    private var onDone: ((Sample?) -> Unit)? = null
    private var startNanos = 0L
    private var count = 0
    private var sumX = 0.0
    private var sumY = 0.0
    private var sumZ = 0.0
    private var meanMagnitude = 0.0
    private var m2 = 0.0
    private var lux: Float? = null

    val isSupported: Boolean get() = accelerometer != null

    val isRunning: Boolean get() = onDone != null

    private val listener = object : SensorEventListener {
        override fun onSensorChanged(event: SensorEvent) {
            when (event.sensor.type) {
                Sensor.TYPE_ACCELEROMETER -> addAcceleration(event)
                // An on-change sensor reports its current value when registered, which is why each burst registers it anew.
                Sensor.TYPE_LIGHT -> lux = event.values[0]
            }
        }

        override fun onAccuracyChanged(sensor: Sensor, accuracy: Int) = Unit
    }

    private val finish = Runnable { complete() }

    /** Starts a burst; [done] gets the result on [handler]'s thread. Ignored while one runs. */
    fun burst(done: (Sample?) -> Unit) {
        val accelerometer = accelerometer ?: return done(null)
        if (isRunning) {
            return
        }

        onDone = done
        startNanos = SystemClock.elapsedRealtimeNanos()
        count = 0
        sumX = 0.0
        sumY = 0.0
        sumZ = 0.0
        meanMagnitude = 0.0
        m2 = 0.0
        lux = null
        sensors.registerListener(listener, accelerometer, SensorManager.SENSOR_DELAY_NORMAL, handler)
        light?.let { sensors.registerListener(listener, it, SensorManager.SENSOR_DELAY_NORMAL, handler) }
        handler.postDelayed(finish, BURST_MS)
    }

    /** Stops a running burst without a result. */
    fun cancel() {
        if (!isRunning) {
            return
        }

        handler.removeCallbacks(finish)
        sensors.unregisterListener(listener)
        onDone = null
    }

    private fun complete() {
        sensors.unregisterListener(listener)
        val done = onDone ?: return
        onDone = null
        done(
            if (count < MIN_SAMPLES) null
            else Sample(
                (sumX / count).toFloat(), (sumY / count).toFloat(), (sumZ / count).toFloat(),
                (m2 / (count - 1)).toFloat(), lux,
            ),
        )
    }

    // Only readings made during the burst: a batch the CPU delivered late carries its own (older) time stamps.
    private fun addAcceleration(event: SensorEvent) {
        if (event.timestamp < startNanos - TOLERANCE_NANOS || event.timestamp > startNanos + BURST_MS * 1_000_000L + TOLERANCE_NANOS) {
            return
        }

        val x = event.values[0]
        val y = event.values[1]
        val z = event.values[2]
        sumX += x
        sumY += y
        sumZ += z
        count++
        // Welford's running variance of the magnitude.
        val magnitude = Math.sqrt((x * x + y * y + z * z).toDouble())
        val delta = magnitude - meanMagnitude
        meanMagnitude += delta / count
        m2 += delta * (magnitude - meanMagnitude)
    }

    companion object {
        /** How long a burst reads: about 25 samples at the normal rate. */
        const val BURST_MS = 5_000L

        /** Fewer accelerometer samples than this: no reading. */
        const val MIN_SAMPLES = 10
        private const val TOLERANCE_NANOS = 500_000_000L
    }
}
