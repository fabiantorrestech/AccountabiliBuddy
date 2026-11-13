package com.accountabilibuddy.shared.sync

/**
 * What the phone leaves out of a sync file, as the PC does (its SyncCoordinator's kept copy and SyncFileBuilder.ForQr): the shared
 * task lists and their tombstones (ADR 48, 72), ADR 76's task parts and the past keys (ADR 56) never go in the copy kept of another
 * device's file (they merge into this phone's own sealed stores, so their text isn't kept twice), nor in a QR file.
 */
object SyncFileStrip {
    /**
     * The copy kept of a received file. [parkedMark]: an empty `parked` list kept as the mark that the device keeps the shared lists
     * (null for none); [steps]: the step counts to keep (a QR file never carries them).
     */
    fun keptCopy(file: SyncFile, parkedMark: List<SyncParked>?, steps: SyncSteps?): SyncFile = file.copy(
        nextTasks = null, parked = parkedMark, later = null, pastKeys = null, steps = steps, taskHistory = null, pastSettled = null,
        autoSaved = null, handovers = null, handoverAcks = null, taskParts = null,
    )

    /** This phone's own file as QR codes carry it: no shared lists, past keys, categories, steps or task parts. */
    fun forQr(file: SyncFile): SyncFile = file.copy(nextTasks = null, parked = null, later = null, pastKeys = null, categories = null, steps = null, taskParts = null)
}
