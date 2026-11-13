package com.accountabilibuddy.shared.tasks

/**
 * One tile of the bottom bar's picker (board Tasks-Phone-Drag-Pick): the [page] a release on it drops on, its second line
 * ([detail]: "2 tasks", or "You are here") and whether it is the page showing ([here]: dimmed, dashed, takes no drop).
 */
data class ShelfPickerTile(val page: ShelfPage, val detail: String, val here: Boolean)
