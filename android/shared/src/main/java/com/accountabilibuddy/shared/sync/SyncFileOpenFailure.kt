package com.accountabilibuddy.shared.sync

/** Why a sealed sync file did not open (ADR 86; the PC's `SyncFileOpenFailure`). */
enum class SyncFileOpenFailure(val code: String) {
    /** Not a sync file, cut off, changed, another key, or JSON that isn't a complete sync file. */
    DAMAGED("damaged"),

    /** An "ABS1" file whose header names another group: a leftover from before a device removal. */
    OTHER_GROUP("otherGroup"),

    /** Opens, but its JSON is over the size limit. */
    TOO_BIG("tooBig"),

    /** Written by a newer app (a format version above this app's). */
    NEWER_FORMAT("newerFormat"),
}

/** What [SyncFileCodec.tryOpen] found: the [file], or why it did not open ([failure]). */
data class SyncFileOpenResult(val file: SyncFile?, val failure: SyncFileOpenFailure?)
