package com.accountabilibuddy.companion.ui.theme

/**
 * A city from the bundled GeoNames list (assets/cities.tsv, the PC's file).
 * [admin1] is GeoNames' first-level region code; many countries use numbers, which are not shown.
 */
data class City(val id: Int, val name: String, val admin1: String, val countryCode: String, val point: GeoPoint) {
    /** "Name, Region, CC", leaving out a region that is only a number. */
    val display: String
        get() = if (admin1.isEmpty() || admin1.all { it.isDigit() }) "$name, $countryCode" else "$name, $admin1, $countryCode"
}
