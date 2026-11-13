package com.accountabilibuddy.shared.tasks

/**
 * The phone Shelf's paging rules (round 2 R7.3, change note §2 and §4c): which page Tasks opens on, a page's neighbours (with or
 * without wrap around), and when the dots under the card become a count. Pure, so the checks can call it; what is stored (the order,
 * the start page, wrap around) is per grouping and device-local.
 */
object ShelfPaging {
    /** Past this many pages the dots become a count pill ("4 / 9"). */
    const val MAX_DOTS = 5

    /** How far a swipe on the header or empty space travels before the page changes (dp). */
    const val SWIPE_TRAVEL_DP = 40

    /** How far a swipe on a row travels before it is the row's own action (dp). */
    const val ROW_SWIPE_DP = 12

    private const val NEXT_KEY = "bin:next"

    /** The page Tasks opens on when none is stored (or the stored one isn't shown): Next for bins, else the middle one. */
    fun defaultStart(keys: List<String>): Int = when {
        keys.isEmpty() -> 0
        NEXT_KEY in keys -> keys.indexOf(NEXT_KEY)
        else -> (keys.size - 1) / 2
    }

    /** The index of the start page among [keys]: [storedStart] when it is shown, else [defaultStart]. */
    fun startIndex(keys: List<String>, storedStart: String?): Int {
        val stored = if (storedStart == null) -1 else keys.indexOf(storedStart)
        return if (stored >= 0) stored else defaultStart(keys)
    }

    /**
     * The page [step] away (-1 left, 1 right) from [index] of [count] pages; null when there is none: an end with [wrap] off (no
     * peek, no drop target, a rubber-band bounce), or a single page. With [wrap] on the far page is the neighbour.
     */
    fun neighbour(index: Int, count: Int, step: Int, wrap: Boolean): Int? {
        if (count <= 1 || index !in 0 until count) return null
        val to = index + step
        if (to in 0 until count) return to
        return if (wrap) Math.floorMod(to, count) else null
    }

    /** Whether the indicator is a count pill instead of dots. */
    fun usesCount(count: Int): Boolean = count > MAX_DOTS

    /** "4 / 9". */
    fun countText(index: Int, count: Int): String = "${index + 1} / $count"

    /** The Options row's value for Page order: "Start on Next · wrap around off". */
    fun summary(startTitle: String?, wrap: Boolean): String =
        (if (startTitle == null) "" else "Start on $startTitle · ") + "wrap around " + (if (wrap) "on" else "off")

    /**
     * The index to show after the pages changed from [oldKeys] to [newKeys] while the page [current] was showing: the same page when it
     * is still there, else the nearest index that exists (never a jump back to the start page while the user is looking).
     */
    fun keep(oldKeys: List<String>, newKeys: List<String>, current: Int): Int {
        if (newKeys.isEmpty()) return 0
        val key = oldKeys.getOrNull(current)
        val same = if (key == null) -1 else newKeys.indexOf(key)
        return if (same >= 0) same else current.coerceIn(0, newKeys.size - 1)
    }
}
