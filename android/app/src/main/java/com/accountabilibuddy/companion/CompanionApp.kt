package com.accountabilibuddy.companion

import android.app.Application
import com.accountabilibuddy.companion.data.CompanionStore
import com.accountabilibuddy.companion.notify.Refresher
import java.io.File

/** The app's single store, and the refresh loop for the notification, widget and bedtime reminder. */
class CompanionApp : Application() {
    lateinit var store: CompanionStore
        private set

    override fun onCreate() {
        super.onCreate()
        store = CompanionStore(File(filesDir, "companion"))
        Refresher.start(this)
    }
}
