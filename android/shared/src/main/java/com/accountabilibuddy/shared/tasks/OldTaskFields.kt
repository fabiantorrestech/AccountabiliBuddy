package com.accountabilibuddy.shared.tasks

import com.accountabilibuddy.shared.sync.SyncAutoSavedItem
import com.accountabilibuddy.shared.sync.SyncCategory
import com.accountabilibuddy.shared.sync.SyncComeback
import com.accountabilibuddy.shared.sync.SyncLaterItem
import com.accountabilibuddy.shared.sync.SyncNextTask
import com.accountabilibuddy.shared.sync.SyncParked

/**
 * The sync file's old task fields (ADR 48, 50, 62, 72; the PC's OldTaskFields), which apps older than ADR 76 read and write: kept
 * for one release beside the `tasks` part. Rebuilt from the task list on every write ([OldTaskFieldsWriter]), never stored on their own.
 *
 * @property nextTasks This session, Queued and today's Finished.
 * @property later Later and Next (marked [SyncLaterItem.NEXT_BIN]), with History's recently finished ones ticked.
 * @property parked To sort, with History's recently finished ones ticked.
 * @property comebacks The open To sort tasks, for the oldest phones.
 * @property categories The labels as categories: name and opaque colour.
 * @property autoSaved Open To sort tasks that came from Auto-saved, within its 30 days; they are in no other old list.
 */
data class OldTaskFields(
    val nextTasks: List<SyncNextTask>,
    val later: List<SyncLaterItem>,
    val parked: List<SyncParked>,
    val comebacks: List<SyncComeback>,
    val categories: List<SyncCategory>,
    val autoSaved: List<SyncAutoSavedItem>,
)
