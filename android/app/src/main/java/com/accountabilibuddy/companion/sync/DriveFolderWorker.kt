package com.accountabilibuddy.companion.sync

import android.content.Context
import androidx.work.CoroutineWorker
import androidx.work.WorkerParameters
import com.accountabilibuddy.companion.CompanionApp

/** The background sync of the sync folder and Google Drive, every 15 minutes while either switch is on ([DriveFolderSource.schedule]). */
class DriveFolderWorker(context: Context, parameters: WorkerParameters) : CoroutineWorker(context, parameters) {
    override suspend fun doWork(): Result {
        val app = applicationContext as CompanionApp
        app.folderSource.syncNow()
        app.driveSource.syncNow()
        return Result.success()
    }
}
