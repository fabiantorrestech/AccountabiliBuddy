package com.accountabilibuddy.companion.sync

import android.content.Context
import com.accountabilibuddy.shared.TokenVault
import com.accountabilibuddy.shared.sync.SyncCategory
import com.accountabilibuddy.shared.sync.SyncFileJson
import com.accountabilibuddy.shared.sync.TaskCategoryPresets
import java.time.Instant
import java.time.temporal.ChronoUnit
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.serialization.json.Json
import com.accountabilibuddy.companion.tasks.PhoneTaskStore
import com.accountabilibuddy.shared.tasks.TaskFacadeLists
import com.accountabilibuddy.shared.tasks.TaskLabelEdits

/**
 * This phone's own task categories (ADR 62): the ones made or recoloured here, by name, with when the colour was chosen. They go in this
 * phone's sync file (only these, so the PCs' categories don't come back to them under the phone's name), and the newest colour for a
 * name wins on every device. The names are the user's text: in the app's private prefs, sealed with a key in the Android Keystore
 * ([TokenVault]), never logged. Like the shared lists they aren't in a backup; a rename or delete here changes no other device's own list.
 *
 * R7.2 (ADR 76, carry item 16): once the task list is the source ([attach]) the categories are its labels: the list shown is the live
 * labels, and add, recolour, rename and delete are label edits ([TaskLabelEdits]) that sync in the `labels` part (and the old
 * `categories` field rebuilt from them). The own list stored here is no longer written (kept as it was, the way back).
 */
class TaskCategoriesStore(context: Context, private val now: () -> Instant = { Instant.now().truncatedTo(ChronoUnit.MILLIS) }) {
    private val prefs = context.getSharedPreferences("task_categories", Context.MODE_PRIVATE)
    private val _categories = MutableStateFlow(read())

    val categories: StateFlow<List<SyncCategory>> = _categories.asStateFlow()

    /** Set by the app: the list changed here, so it goes to the other devices soon. */
    var onChanged: () -> Unit = {}

    private var model: PhoneTaskStore? = null
    private val modelActive: Boolean get() = model?.isActive == true

    /** R7.2: the labels of the task list become the categories (while it is the source). */
    fun attach(store: PhoneTaskStore) {
        model = store
        store.addListener { if (store.isActive) _categories.value = TaskFacadeLists.categories(store.lists.value) }
        if (store.isActive) _categories.value = TaskFacadeLists.categories(store.lists.value)
        // R7.2 review item 2: a closed task list has no labels to show, and the own list (stale since the migration) isn't used.
        if (store.isClosed) _categories.value = emptyList()
    }

    /** The list for this phone's sync file; an empty list says it has none, which clears a receiver's copy. */
    fun forSync(): List<SyncCategory> = model?.fileParts()?.first?.categories ?: _categories.value

    // A label edit in the task list (R7.2); true when it was made there.
    // A closed task list takes no edit, and the own list is never written again after the migration: the edit is dropped.
    private fun inModel(change: (com.accountabilibuddy.shared.tasks.TaskLists, String, java.time.OffsetDateTime) -> com.accountabilibuddy.shared.tasks.TaskLists): Boolean {
        if (model?.isClosed == true) return true
        val store = model?.takeIf { it.isActive } ?: return false
        if (store.change(change)) onChanged()
        return true
    }

    /** A category of [name] in the next palette colour none of [taken] has; a name already listed (any case) stays as it is. */
    @Synchronized
    fun add(name: String, taken: Collection<String>): SyncCategory {
        val trimmed = name.trim()
        _categories.value.firstOrNull { it.name.equals(trimmed, ignoreCase = true) }?.let { return it }
        val color = TaskCategoryPresets.PALETTE.firstOrNull { candidate -> taken.none { it.equals(candidate, ignoreCase = true) } }
            ?: TaskCategoryPresets.PALETTE[taken.size % TaskCategoryPresets.PALETTE.size]
        if (inModel { lists, self, now -> TaskLabelEdits.add(lists, trimmed, color, now, self) }) {
            return _categories.value.firstOrNull { it.name.equals(trimmed, ignoreCase = true) } ?: SyncCategory(trimmed, color)
        }

        val added = SyncCategory(trimmed, color)
        save(_categories.value + added)
        return added
    }

    /** Chooses [name]'s colour, stamped so it wins on every device; a category only another device lists joins this list with it. */
    @Synchronized
    fun recolor(name: String, color: String) {
        if (inModel { lists, self, now ->
                if (TaskLabelEdits.named(lists, name) == null) TaskLabelEdits.add(lists, name, color, now, self) else TaskLabelEdits.recolour(lists, name, color, now, self)
            }) return
        val stamped = SyncCategory(name, color, now())
        val list = _categories.value
        save(if (list.any { it.name.equals(name, ignoreCase = true) }) list.map { if (it.name.equals(name, ignoreCase = true)) stamped else it } else list + stamped)
    }

    @Synchronized
    fun rename(oldName: String, newName: String, color: String?) {
        if (inModel { lists, self, now ->
                if (TaskLabelEdits.named(lists, oldName) == null) TaskLabelEdits.add(lists, newName, color, now, self) else TaskLabelEdits.rename(lists, oldName, newName, now, self)
            }) return
        val list = _categories.value
        val renamed = list.firstOrNull { it.name.equals(oldName, ignoreCase = true) }?.copy(name = newName)
            ?: SyncCategory(newName, color ?: TaskCategoryPresets.PALETTE.first())
        save(list.filterNot { it.name.equals(oldName, ignoreCase = true) } + renamed)
    }

    @Synchronized
    fun delete(name: String) {
        if (inModel { lists, self, now -> TaskLabelEdits.delete(lists, name, now, self) }) return
        save(_categories.value.filterNot { it.name.equals(name, ignoreCase = true) })
    }

    private fun save(list: List<SyncCategory>) {
        prefs.edit().putString(KEY, TokenVault.seal(SyncFileJson.writeCategories(list).toString(), ALIAS)).apply()
        _categories.value = list
        onChanged()
    }

    // Empty when nothing is stored or it can't be opened (the key goes with the app's data).
    private fun read(): List<SyncCategory> {
        val json = prefs.getString(KEY, null)?.let { TokenVault.open(it, ALIAS) } ?: return emptyList()
        return runCatching { SyncFileJson.readCategories(Json.parseToJsonElement(json)) }.getOrNull().orEmpty()
    }

    private companion object {
        const val KEY = "list"
        const val ALIAS = "accountabilibuddy-task-categories"
    }
}
