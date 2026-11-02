package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.RowScope
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.foundation.selection.selectable
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.runtime.DisposableEffect
import androidx.compose.ui.draw.clip
import androidx.compose.ui.unit.sp
import com.accountabilibuddy.companion.ui.theme.AbColors
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.OutlinedTextFieldDefaults
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TimePicker
import androidx.compose.material3.TimePickerDefaults
import androidx.compose.material3.rememberTimePickerState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.ui.kit.AbCard
import com.accountabilibuddy.companion.ui.kit.AbDialog
import com.accountabilibuddy.companion.ui.kit.AbInsetRow
import com.accountabilibuddy.companion.ui.kit.AbListRow
import com.accountabilibuddy.companion.ui.kit.AbSectionLabel
import com.accountabilibuddy.companion.ui.kit.AbSegmented
import com.accountabilibuddy.companion.ui.kit.AbSheet
import com.accountabilibuddy.companion.ui.kit.AbSubpageHeader
import com.accountabilibuddy.companion.ui.kit.AbTonalButton
import com.accountabilibuddy.companion.ui.theme.Ab
import com.accountabilibuddy.companion.ui.theme.AbPalettes
import com.accountabilibuddy.companion.ui.theme.AbShapes
import com.accountabilibuddy.companion.ui.theme.AbTypography
import com.accountabilibuddy.companion.ui.theme.AppearancePlace
import com.accountabilibuddy.companion.ui.theme.AppearanceSummary
import com.accountabilibuddy.companion.ui.theme.City
import com.accountabilibuddy.companion.ui.theme.CityIndex
import com.accountabilibuddy.companion.ui.theme.ColourMode
import com.accountabilibuddy.companion.ui.theme.GeoPoint
import com.accountabilibuddy.companion.ui.theme.LightDarkMode
import com.accountabilibuddy.companion.ui.theme.ThemeName
import com.accountabilibuddy.companion.ui.theme.rememberDarkPlan
import java.time.LocalTime
import java.time.ZonedDateTime

/** Which time of day the picker is editing. */
private enum class TimeField { DarkFrom, LightFrom }

/**
 * Settings → Appearance: the theme, light or dark (Light, Dark, System, At a set time, After sunset), the colour setting,
 * then animation speed and the timeline look. The theme is per device and never synced; the place for After sunset is
 * typed or picked from a city list (no location permission), kept sealed on this phone and clearable here (ADR 49).
 */
@Composable
fun AppearanceScreen(onBack: () -> Unit) {
    val context = LocalContext.current
    val themeName by UiPrefs.themeName.collectAsState()
    val mode by UiPrefs.mode.collectAsState()
    val colour by UiPrefs.colour.collectAsState()
    val darkAt by UiPrefs.darkAt.collectAsState()
    val lightAt by UiPrefs.lightAt.collectAsState()
    val place by UiPrefs.place.collectAsState()
    val motion by UiPrefs.motion.collectAsState()
    val barLook by UiPrefs.barLook.collectAsState()
    val plan = rememberDarkPlan(mode, darkAt, lightAt, place)

    var editingTime by remember { mutableStateOf<TimeField?>(null) }
    var pickingPlace by remember { mutableStateOf(false) }

    Column(
        Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(horizontal = 16.dp, vertical = 8.dp),
        verticalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        AbSubpageHeader(crumb = "Settings", title = "Appearance", onBack = onBack)

        AbCard {
            AbSectionLabel("Theme")
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                ThemeName.entries.forEach { theme ->
                    SwatchPreview(theme, selected = theme == themeName, onClick = { UiPrefs.setThemeName(context, theme) }, modifier = Modifier.weight(1f))
                }
            }
            // The names are their own row, aligned on their text baselines, since each theme's font has its own metrics.
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                ThemeName.entries.forEach { theme ->
                    SwatchName(theme, selected = theme == themeName, onClick = { UiPrefs.setThemeName(context, theme) }, modifier = Modifier.weight(1f))
                }
            }
        }

        AbCard {
            AbSectionLabel("Light or dark")
            val fixed = listOf(LightDarkMode.Light, LightDarkMode.Dark, LightDarkMode.System)
            val timed = listOf(LightDarkMode.SetTime, LightDarkMode.Sunset)
            AbSegmented(fixed.map { it.label }, fixed.indexOf(mode).takeIf { it >= 0 }, { UiPrefs.setMode(context, fixed[it]) })
            AbSegmented(timed.map { it.label }, timed.indexOf(mode).takeIf { it >= 0 }, { UiPrefs.setMode(context, timed[it]) })
            Text(
                AppearanceSummary.build(themeName, mode, darkAt, lightAt, plan, ZonedDateTime.now()),
                style = Ab.type.small, color = Ab.colors.muted,
            )
            if (mode == LightDarkMode.SetTime || mode == LightDarkMode.Sunset) {
                val label = if (mode == LightDarkMode.Sunset) "Used when the sun can't be worked out" else null
                if (label != null) Text(label, style = Ab.type.small, color = Ab.colors.muted)
                TimeRow("Dark from", darkAt) { editingTime = TimeField.DarkFrom }
                TimeRow("Light from", lightAt) { editingTime = TimeField.LightFrom }
            }
            // A stored place can be seen and forgotten in every mode, not only while After sunset is chosen.
            if (mode == LightDarkMode.Sunset || place != null) {
                PlaceRow(place, onChange = { pickingPlace = true }, onForget = { UiPrefs.setPlace(context, null) })
            }
            if (mode == LightDarkMode.Sunset) {
                AppearanceSummary.sunsetNote(mode, place != null, plan, darkAt, lightAt)?.let {
                    Text(it, style = Ab.type.small, color = Ab.colors.muted)
                }
            }
        }

        AbCard {
            AbSectionLabel("Colour")
            AbSegmented(ColourMode.entries.map { it.label }, ColourMode.entries.indexOf(colour), { UiPrefs.setColour(context, ColourMode.entries[it]) })
            Text(
                if (colour == ColourMode.Quiet) "Quiet keeps colour for progress and the one main button. Everything else stays ink."
                else "Standard also colours what has a meaning: goal met, waiting, drift.",
                style = Ab.type.small, color = Ab.colors.muted,
            )
        }

        AbCard {
            AbSectionLabel("Animations")
            AbSegmented(MotionSpeed.entries.map { it.label }, MotionSpeed.entries.indexOf(motion), { UiPrefs.setMotion(context, MotionSpeed.entries[it]) })
            AbSectionLabel("Timeline look")
            AbSegmented(
                listOf("Colour", "Grayscale", "E-ink"), BarLook.entries.indexOf(barLook),
                {
                    UiPrefs.setBarLook(context, BarLook.entries[it])
                    (context.applicationContext as CompanionApp).refreshSoon()
                },
            )
            Text(
                when (barLook) {
                    BarLook.Color -> "The widget, the notification and Today draw the timeline in colour."
                    BarLook.Grayscale -> "For a phone kept in grayscale: every block outlined, and the now line ringed so it shows on anything."
                    BarLook.Eink -> "For e-ink screens: black and white, with no see-through layers."
                },
                style = Ab.type.small, color = Ab.colors.muted,
            )
        }
    }

    editingTime?.let { field ->
        TimeDialog(
            title = if (field == TimeField.DarkFrom) "Dark from" else "Light from",
            initial = if (field == TimeField.DarkFrom) darkAt else lightAt,
            onDismiss = { editingTime = null },
            onPick = { picked ->
                if (field == TimeField.DarkFrom) UiPrefs.setSetTimes(context, picked, lightAt) else UiPrefs.setSetTimes(context, darkAt, picked)
                editingTime = null
            },
        )
    }

    if (pickingPlace) {
        PlaceSheet(
            onDismiss = { pickingPlace = false },
            onPick = { picked ->
                UiPrefs.setPlace(context, picked)
                pickingPlace = false
            },
        )
    }
}

/** One theme's colours: a light half and a dark half. Selected is an ink edge (accent is kept for progress and the main button). */
@Composable
private fun SwatchPreview(theme: ThemeName, selected: Boolean, onClick: () -> Unit, modifier: Modifier = Modifier) {
    val light = AbPalettes.of(theme, dark = false)
    val dark = AbPalettes.of(theme, dark = true)
    val shapes = AbShapes.of(theme)
    val edge = if (selected) BorderStroke(2.dp, Ab.colors.ink) else BorderStroke(1.dp, Ab.colors.line)
    Row(
        modifier.height(96.dp).clip(shapes.mediumShape).border(edge, shapes.mediumShape)
            .selectable(selected = selected, role = Role.RadioButton, onClick = onClick),
    ) {
        SwatchHalf(light, shapes, Modifier.weight(1f).fillMaxHeight())
        SwatchHalf(dark, shapes, Modifier.weight(1f).fillMaxHeight())
    }
}

/** A theme's name in its own display font; tapping it picks the theme too. */
@Composable
private fun RowScope.SwatchName(theme: ThemeName, selected: Boolean, onClick: () -> Unit, modifier: Modifier = Modifier) {
    Text(
        theme.label,
        modifier = modifier.alignByBaseline().clickable(onClick = onClick).padding(vertical = 4.dp),
        style = AbTypography.of(theme).section.copy(fontSize = 15.sp),
        color = Ab.colors.ink,
        fontWeight = if (selected) FontWeight.Bold else null,
        textAlign = TextAlign.Center,
        maxLines = 1,
    )
}

@Composable
private fun SwatchHalf(colors: AbColors, shapes: AbShapes, modifier: Modifier) {
    Column(modifier.background(colors.bg).padding(10.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
        Box(Modifier.fillMaxWidth().height(22.dp).background(colors.surface, shapes.smallShape))
        Box(Modifier.size(14.dp).background(colors.accent, CircleShape))
    }
}

@Composable
private fun TimeRow(label: String, time: LocalTime, onClick: () -> Unit) {
    AbInsetRow(onClick = onClick) {
        Text(label, style = Ab.type.body, color = Ab.colors.ink, modifier = Modifier.weight(1f))
        Text(AppearanceSummary.format(time), style = Ab.type.body, fontWeight = FontWeight.SemiBold, color = Ab.colors.ink)
    }
}

@Composable
private fun PlaceRow(place: AppearancePlace?, onChange: () -> Unit, onForget: () -> Unit) {
    AbInsetRow {
        Column(Modifier.weight(1f)) {
            Text("Place for sunset", style = Ab.type.small, color = Ab.colors.muted)
            Text(place?.label ?: "None yet", style = Ab.type.body, fontWeight = FontWeight.Medium, color = Ab.colors.ink)
        }
        TextButton(onClick = onChange) { Text(if (place == null) "Set" else "Change", color = Ab.colors.ink, fontWeight = FontWeight.SemiBold) }
        if (place != null) TextButton(onClick = onForget) { Text("Forget", color = Ab.colors.muted) }
    }
}

/** A 12-hour time picker in a dialog. */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun TimeDialog(title: String, initial: LocalTime, onDismiss: () -> Unit, onPick: (LocalTime) -> Unit) {
    val state = rememberTimePickerState(initialHour = initial.hour, initialMinute = initial.minute, is24Hour = false)
    val colors = Ab.colors
    AbDialog(title = title, onDismiss = onDismiss, confirmText = "Set", onConfirm = { onPick(LocalTime.of(state.hour, state.minute)) }) {
        TimePicker(
            state = state,
            colors = TimePickerDefaults.colors(
                clockDialColor = colors.surface2,
                clockDialSelectedContentColor = colors.onAccent,
                clockDialUnselectedContentColor = colors.ink,
                selectorColor = colors.accent,
                periodSelectorBorderColor = colors.line,
                periodSelectorSelectedContainerColor = colors.surface2,
                periodSelectorUnselectedContainerColor = colors.surface,
                periodSelectorSelectedContentColor = colors.ink,
                periodSelectorUnselectedContentColor = colors.muted,
                timeSelectorSelectedContainerColor = colors.surface2,
                timeSelectorUnselectedContainerColor = colors.surface2,
                timeSelectorSelectedContentColor = colors.ink,
                timeSelectorUnselectedContentColor = colors.muted,
            ),
        )
    }
}

/**
 * Pick a city from the bundled list, or type a latitude and longitude. Nothing is looked up and no location permission is
 * asked: the place never leaves the phone.
 */
@Composable
private fun PlaceSheet(onDismiss: () -> Unit, onPick: (AppearancePlace) -> Unit) {
    val context = LocalContext.current
    val index = remember { CityIndex(context.applicationContext) }
    var query by remember { mutableStateOf("") }
    var results by remember { mutableStateOf<List<City>>(emptyList()) }
    var latitude by remember { mutableStateOf("") }
    var longitude by remember { mutableStateOf("") }
    var error by remember { mutableStateOf<String?>(null) }

    LaunchedEffect(query) { results = index.search(query) }
    DisposableEffect(Unit) { onDispose { index.release() } }

    AbSheet(onDismiss = onDismiss) {
        Column(Modifier.imePadding().verticalScroll(rememberScrollState()), verticalArrangement = Arrangement.spacedBy(10.dp)) {
            Text("Place for sunset", style = Ab.type.section, color = Ab.colors.ink)
            Text(
                "Used only on this phone to work out sunrise and sunset. It is kept sealed here, never synced or sent, and you can forget it any time.",
                style = Ab.type.small, color = Ab.colors.muted,
            )
            AbSectionLabel("Pick a city")
            OutlinedTextField(
                value = query, onValueChange = { query = it }, singleLine = true, placeholder = { Text("Start typing a city") },
                modifier = Modifier.fillMaxWidth(), colors = fieldColors(),
            )
            results.forEachIndexed { position, city ->
                AbListRow(
                    title = city.display, onClick = { onPick(AppearancePlace(city.point, city.display)) },
                    showChevron = false, divider = position < results.lastIndex,
                )
            }
            AbSectionLabel("Or type latitude and longitude")
            Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                OutlinedTextField(
                    value = latitude, onValueChange = { latitude = it; error = null }, singleLine = true, label = { Text("Latitude") },
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Ascii),
                    modifier = Modifier.weight(1f), colors = fieldColors(),
                )
                OutlinedTextField(
                    value = longitude, onValueChange = { longitude = it; error = null }, singleLine = true, label = { Text("Longitude") },
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Ascii),
                    modifier = Modifier.weight(1f), colors = fieldColors(),
                )
            }
            error?.let { Text(it, style = Ab.type.small, color = Ab.colors.drift) }
            AbTonalButton("Use these numbers", onClick = {
                val point = GeoPoint(parseDegrees(latitude), parseDegrees(longitude))
                if (point.isValid) onPick(AppearancePlace(point, "Typed in")) else error = "Use numbers like 41.88 and -87.63: latitude from -90 to 90, longitude from -180 to 180."
            }, modifier = Modifier.fillMaxWidth())
        }
    }
}

@Composable
private fun fieldColors() = OutlinedTextFieldDefaults.colors(
    focusedBorderColor = Ab.colors.ink,
    unfocusedBorderColor = Ab.colors.line,
    focusedTextColor = Ab.colors.ink,
    unfocusedTextColor = Ab.colors.ink,
    cursorColor = Ab.colors.ink,
    focusedLabelColor = Ab.colors.muted,
    unfocusedLabelColor = Ab.colors.muted,
    focusedPlaceholderColor = Ab.colors.faint,
    unfocusedPlaceholderColor = Ab.colors.faint,
)

/** A typed degree value: a decimal comma or a typographic minus is accepted; NaN when it isn't a number. */
private fun parseDegrees(text: String): Double =
    text.trim().replace(',', '.').replace('−', '-').toDoubleOrNull() ?: Double.NaN
