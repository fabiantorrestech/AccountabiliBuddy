package com.accountabilibuddy.companion.ui.sheets

/** When a sheet refuses to close (FX-A): only a close of a guarded flow that holds changes is refused. Pure (checked on its own). */
object SheetDiscardRules {
    fun refuses(guarded: Boolean, dirty: Boolean): Boolean = guarded && dirty
}
