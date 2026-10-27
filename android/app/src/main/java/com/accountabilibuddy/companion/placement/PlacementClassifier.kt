package com.accountabilibuddy.companion.placement

import kotlin.math.abs

/**
 * Turns one [PlacementReading] into a [PlacementState]. Pure: no sensors, no clock. Anything that is not clearly one
 * state is [PlacementState.Unknown]; a missing reading never becomes "out".
 *
 * The thresholds are first guesses to tune on the Pixel 7.
 */
object PlacementClassifier {
    /** Brighter than this with the screen off and the phone flat: lying face up in the open. */
    const val FACE_UP_MIN_LUX = 15f

    /** Darker than this (with the proximity sensor covered): bag or pocket. */
    const val DARK_MAX_LUX = 5f

    /** Gravity along z beyond this (m/s², out of 9.8): lying flat, screen up (+) or down (-). */
    const val FLAT_GRAVITY = 8.5f

    /** Gravity along y beyond this, and larger than x and z: upright, as in a pocket. */
    const val UPRIGHT_GRAVITY = 6.5f

    /** Variance of the accelerometer magnitude at or below this: the phone is still. */
    const val STILL_VARIANCE = 0.05f

    fun classify(r: PlacementReading): PlacementState {
        if (r.inCallOrEmergency) {
            return PlacementState.Unknown
        }

        if (r.screenInteractive && r.unlocked) {
            return if (r.essentialInFront) PlacementState.Unknown else PlacementState.InUse
        }

        val near = r.proximityNear ?: return PlacementState.Unknown
        val x = r.gravityX ?: return PlacementState.Unknown
        val y = r.gravityY ?: return PlacementState.Unknown
        val z = r.gravityZ ?: return PlacementState.Unknown
        val still = (r.variance ?: return PlacementState.Unknown) <= STILL_VARIANCE
        val lux = r.lux
        if (!near) {
            val faceUp = !r.screenInteractive && lux != null && lux > FACE_UP_MIN_LUX && z > FLAT_GRAVITY && still
            return if (faceUp) PlacementState.FaceUp else PlacementState.Unknown
        }

        if (z < -FLAT_GRAVITY && still) {
            return PlacementState.Desk
        }

        // Bag and pocket both need the dark; a missing light reading is not "dark".
        if (lux == null || lux >= DARK_MAX_LUX) {
            return PlacementState.Unknown
        }

        val upright = abs(y) > UPRIGHT_GRAVITY && abs(y) > abs(x) && abs(y) > abs(z)
        return if (upright || !still) PlacementState.Pocket else PlacementState.Bag
    }
}
