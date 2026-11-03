package com.accountabilibuddy.companion.sync

import android.content.Context
import androidx.work.CoroutineWorker
import androidx.work.WorkerParameters
import com.accountabilibuddy.companion.CompanionApp

/** The background sync of the sync folder, Google Drive and the relay, every 15 minutes while any switch is on ([DriveFolderSource.schedule]). */
class DriveFolderWorker(context: Context, parameters: WorkerParameters) : CoroutineWorker(context, parameters) {
    override suspend fun doWork(): Result {
        val app = applicationContext as CompanionApp
        // Daily steps first, so a fresh count goes out in this same pass (a no-op unless the switch is on, a read is due and Health
        // Connect's background-read permission was granted; the syncs below send the file, so the store does not).
        runCatching { app.steps.readIfDue(inBackground = !app.appOnScreen, notify = false) }
        app.folderSource.syncNow()
        app.driveSource.syncNow()
        app.relaySource.syncNow()
        return Result.success()
    }
}
