package com.accountabilibuddy.companion.ui.theme

/** A place on Earth in degrees: latitude north positive, longitude east positive. Private data (ADR 49). */
data class GeoPoint(val latitude: Double, val longitude: Double) {
    val isValid: Boolean
        get() = latitude in -90.0..90.0 && longitude in -180.0..180.0
}
