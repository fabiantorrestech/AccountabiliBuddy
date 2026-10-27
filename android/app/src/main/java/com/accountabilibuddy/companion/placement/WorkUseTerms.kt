package com.accountabilibuddy.companion.placement

/** What the next work use takes: a note of at least [minNoteLength] characters, and a wait of [waitSeconds] first (0 = none). */
data class WorkUseTerms(val minNoteLength: Int, val waitSeconds: Int)
