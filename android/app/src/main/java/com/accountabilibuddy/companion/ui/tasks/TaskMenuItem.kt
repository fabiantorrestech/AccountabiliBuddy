package com.accountabilibuddy.companion.ui.tasks

import androidx.compose.ui.graphics.vector.ImageVector

/** One entry of a row's ⋯ menu: a Material icon, a label and what it does. */
class TaskMenuItem(val label: String, val icon: ImageVector, val onClick: () -> Unit)
