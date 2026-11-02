package com.accountabilibuddy.companion.ui.settings

import androidx.compose.runtime.Composable

/** One row of a Settings page: its [key] (a [SettingsAnchors] name, so search can scroll to it) and what it draws. */
class PageRow(val key: String, val content: @Composable () -> Unit)
