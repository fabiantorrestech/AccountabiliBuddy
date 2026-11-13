package com.accountabilibuddy.shared.tasks

/**
 * The words and lives of the Tasks page's toasts (round 2 R7.3b; board Tasks-Phone-Moved; bundle spec §14): "2 tasks moved to
 * Later" / "Tap to see them there", "1 task added to Today · Queued" / "Tap to see it in Today". Pure, so the checks read them.
 */
object ShelfToasts {
    /** How long the rows a toast led to stay highlighted (the prototype's 1.4 s). */
    const val FLASH_MS = 1_400L

    /** The longest title quoted in a toast before it is cut with "…". */
    const val SHORT_TITLE = 28

    /** The toast's life: an explanation 7 s, one with Undo (or an action) 6 s, else 4 s. */
    fun lengthOf(canUndo: Boolean, explains: Boolean = false): ShelfToastLength = when {
        explains -> ShelfToastLength.EXPLANATION
        canUndo -> ShelfToastLength.WITH_UNDO
        else -> ShelfToastLength.PLAIN
    }

    /** "1 task", "3 tasks". */
    fun tasksWord(count: Int): String = if (count == 1) "1 task" else "$count tasks"

    /** "2 tasks moved to Later" for a Shelf bin, "2 tasks added to Today · Queued" for Today. */
    fun moved(count: Int, bin: TaskBin): String =
        if (TaskBins.isShelf(bin)) "${tasksWord(count)} moved to ${TaskBins.name(bin)}" else "${tasksWord(count)} added to ${TaskBins.place(bin)}"

    /** A drop on a page: its bin as [moved]; a label "2 tasks moved to ePT"; a tag "2 tasks tagged #Release". */
    fun dropped(count: Int, page: ShelfPage): String = when {
        page.bin != null -> moved(count, page.bin)
        page.tag != null -> "${tasksWord(count)} tagged ${page.title}"
        else -> "${tasksWord(count)} moved to ${page.title}"
    }

    /** The second line: "Tap to see them there", "Tap to see it in Today". */
    fun see(count: Int, inToday: Boolean): String = "Tap to see ${them(count)} " + (if (inToday) "in Today" else "there")

    /** The tap's name for a screen reader: "Show them in Later". */
    fun show(count: Int, where: String): String = "Show ${them(count)} in $where"

    /** A title short enough to quote. */
    fun short(title: String): String = if (title.length <= SHORT_TITLE) title else title.take(SHORT_TITLE - 1).trimEnd() + "…"

    private fun them(count: Int): String = if (count == 1) "it" else "them"
}
