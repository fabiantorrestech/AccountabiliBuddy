package com.accountabilibuddy.shared.tasks

/** The bins' names and families (the PC's TaskBins): Today holds This session, Queued and Finished today; the Shelf To sort, Next, Later. */
object TaskBins {
    /** The Shelf's bins, left to right. */
    val SHELF: List<TaskBin> = listOf(TaskBin.SORT, TaskBin.NEXT, TaskBin.LATER)

    fun isShelf(bin: TaskBin): Boolean = bin == TaskBin.SORT || bin == TaskBin.NEXT || bin == TaskBin.LATER

    fun name(bin: TaskBin): String = when (bin) {
        TaskBin.SESSION -> "This session"
        TaskBin.QUEUED -> "Queued"
        TaskBin.DONE -> "Finished today"
        TaskBin.SORT -> "To sort"
        TaskBin.NEXT -> "Next"
        TaskBin.LATER -> "Later"
    }

    /** Where a task is, as a sheet's subtitle says it: "Shelf · Next", "Today · Queued". */
    fun place(bin: TaskBin): String = (if (isShelf(bin)) "Shelf · " else "Today · ") + (if (bin == TaskBin.DONE) "Finished" else name(bin))

    /** A row's bin part when its page isn't a bin (grouped by label, tag or none): "Next", "Today · Queued". */
    fun rowPart(bin: TaskBin): String = if (isShelf(bin)) name(bin) else place(bin)
}
