package com.accountabilibuddy.companion.sync

import android.content.Context
import androidx.work.CoroutineWorker
import androidx.work.WorkerParameters
import com.accountabilibuddy.companion.CompanionApp

/** The sync folder's background sync, every 15 minutes while its switch is on ([DriveFolderSource.schedule]). */
class DriveFolderWorker(context: Context, parameters: WorkerParameters) : CoroutineWorker(context, parameters) {
    override suspend fun doWork(): Result {
        (applicationContext as CompanionApp).folderSource.syncNow()
        return Result.success()
    }
}
