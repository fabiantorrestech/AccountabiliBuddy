package com.accountabilibuddy.companion.ui.settings

import com.accountabilibuddy.companion.ui.SettingsCategory

/**
 * One thing Settings has, for the search: what to show ([title]), where it lives ([category] and the row's [anchor]) and the other
 * words someone might type for it ([keywords]).
 */
class SearchEntry(val title: String, val category: SettingsCategory, val anchor: String, val keywords: List<String> = emptyList())
