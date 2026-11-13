package com.accountabilibuddy.shared.tasks

import com.accountabilibuddy.shared.sync.SyncAutoSavedItem
import com.accountabilibuddy.shared.sync.SyncCategory
import com.accountabilibuddy.shared.sync.SyncLaterItem
import com.accountabilibuddy.shared.sync.SyncNextTask
import com.accountabilibuddy.shared.sync.SyncParked
import java.time.OffsetDateTime
import java.time.ZoneId

/**
 * What the one-time task migration (ADR 76) reads on the phone: the old stores' contents as they are kept today, tombstones and
 * local items included (the PC's TaskMigrationInput). The app loads the stores; the migration itself touches no storage.
 *
 * @property categories The phone's own categories (`TaskCategoriesStore`).
 * @property nextTasks The next session's list (`SharedListsStore`).
 * @property later The Later list.
 * @property comebacks The come-backs (parked thoughts) in their shared form.
 * @property autoSaved The Auto-saved list.
 * @property presets The saved filters (`TaskFilterPresetsStore`).
 * @property now The trusted time of the run.
 * @property zone The local time zone, for "done today" and origin lines.
 * @property isNumberingDevice This device hands out final numbers ([TaskNumbering.numberingDevice]); otherwise numbers are temporary.
 * @property firstNumber The first number to give (above any number already used).
 * @property sessionActions How many actions This session takes (the session cap; 3 by default).
 */
data class TaskMigrationInput(
    val categories: List<SyncCategory>,
    val nextTasks: List<SyncNextTask>,
    val later: List<SyncLaterItem>,
    val comebacks: List<SyncParked>,
    val autoSaved: List<SyncAutoSavedItem>,
    val presets: List<TaskMigrationPreset>,
    val now: OffsetDateTime,
    val zone: ZoneId,
    val isNumberingDevice: Boolean,
    val firstNumber: Int = 1,
    val sessionActions: Int = 3,
)
