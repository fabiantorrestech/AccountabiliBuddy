package com.accountabilibuddy.companion.placement

/**
 * Everything the classifier looks at, gathered for one moment. A null sensor value means "no fresh reading", which
 * the classifier never turns into a guess (it answers [PlacementState.Unknown]).
 *
 * @property screenInteractive The screen is on.
 * @property unlocked The phone is unlocked (no lock screen showing).
 * @property essentialInFront An essential app, or the essentials screen, is in front.
 * @property inCallOrEmergency A call (ringing too) or emergency phone time: nothing is judged then.
 * @property proximityNear The proximity sensor is covered; null when it never reported.
 * @property lux The light level in the burst just taken; null when none arrived.
 * @property gravityX Mean accelerometer x over the burst, in m/s²; null without a burst.
 * @property gravityY Mean accelerometer y; null without a burst.
 * @property gravityZ Mean accelerometer z (positive with the screen up); null without a burst.
 * @property variance Variance of the accelerometer's magnitude over the burst, a measure of movement; null without a burst.
 */
data class PlacementReading(
    val screenInteractive: Boolean,
    val unlocked: Boolean,
    val essentialInFront: Boolean,
    val inCallOrEmergency: Boolean,
    val proximityNear: Boolean?,
    val lux: Float?,
    val gravityX: Float?,
    val gravityY: Float?,
    val gravityZ: Float?,
    val variance: Float?,
)
