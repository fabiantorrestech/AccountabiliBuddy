package com.accountabilibuddy.companion.ui.plan

/**
 * What the Plan pane can ask for. The buttons that change the plan are only offered when [PlanPaneModel.canChange] (a PC takes
 * the edit); the pane never saves anything itself.
 */
data class PlanPaneActions(
    val onPick: (PlanDayCell) -> Unit,
    val onShiftWeek: (Int) -> Unit,
    val onToday: () -> Unit,
    val onChangeDay: () -> Unit,
    val onTemplates: () -> Unit,
    val onCategories: () -> Unit,
    val onSetWeek: (Int) -> Unit,
    val onDiscardEdit: () -> Unit,
)
