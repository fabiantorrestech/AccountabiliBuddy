package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.model.CategoryBehavior
import com.accountabilibuddy.companion.model.DayCategory
import com.accountabilibuddy.companion.model.DayPlan
import java.util.UUID

/** Colours a category can take (tap the dot to go to the next one). */
private val Palette = listOf("#2563EB", "#7C3AED", "#CA8A04", "#0D9488", "#16A34A", "#334155", "#DC2626", "#DB2777", "#EA580C", "#0891B2")

/**
 * The day plan's categories on the phone: rename, recolour, change what the buddy does (Watch, Rest, Sleep), add, and
 * delete one no part uses.
 */
@Composable
fun CategoriesEditor(plan: DayPlan, onChange: (DayPlan) -> Unit, onMessage: (String) -> Unit) {
    fun used(id: UUID): Boolean =
        (plan.templates.flatMap { it.template.parts } + plan.oneOffDays.flatMap { it.template.parts }).any { it.categoryId == id }

    fun update(category: DayCategory) = onChange(plan.copy(categories = plan.categories.map { if (it.id == category.id) category else it }))

    Column(verticalArrangement = Arrangement.spacedBy(10.dp)) {
        Text(
            "Watch: the buddy asks about new sites and counts drift. Rest: no asking. Sleep: bedtime reminder and the shaded night.",
            style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
        for (category in plan.categories) {
            var behaviourMenu by remember(category.id) { mutableStateOf(false) }
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                Box(
                    Modifier
                        .size(26.dp)
                        .background(Color((category.argb).toInt()), CircleShape)
                        .clickable {
                            val next = Palette[(Palette.indexOfFirst { it.equals(category.color, ignoreCase = true) } + 1).mod(Palette.size)]
                            update(category.copy(color = next))
                        },
                )
                OutlinedTextField(
                    value = category.name,
                    onValueChange = { update(category.copy(name = it)) },
                    singleLine = true,
                    modifier = Modifier.weight(1f),
                )
                Box {
                    OutlinedButton(onClick = { behaviourMenu = true }) { Text(category.behavior.name) }
                    DropdownMenu(expanded = behaviourMenu, onDismissRequest = { behaviourMenu = false }) {
                        for (behaviour in CategoryBehavior.entries) {
                            DropdownMenuItem(text = { Text(behaviour.name) }, onClick = {
                                update(category.copy(behavior = behaviour))
                                behaviourMenu = false
                            })
                        }
                    }
                }
                TextButton(onClick = {
                    if (used(category.id)) {
                        onMessage("'${category.name}' is used by a part of a day; give those parts another category first.")
                    } else {
                        onChange(plan.copy(categories = plan.categories.filter { it.id != category.id }))
                    }
                }) { Text("✕") }
            }
        }

        TextButton(onClick = {
            val color = Palette.firstOrNull { candidate -> plan.categories.none { it.color.equals(candidate, ignoreCase = true) } } ?: Palette.first()
            onChange(plan.copy(categories = plan.categories + DayCategory(UUID.randomUUID(), "New category", color, CategoryBehavior.Rest)))
        }) { Text("Add a category") }
    }
}
