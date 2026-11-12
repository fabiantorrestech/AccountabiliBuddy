# 83. Sync the Shelf page order

Date: 2026-11-12

## Status

Proposed

Amends [76. One task list with bins, labels and tags](0076-one-task-list-with-bins-labels-and-tags.md) (one more optional field inside a synced view's config; the `views` part, its merge and the format stay as they are).

## Context

UI round 2 shows the Shelf's groups side by side on the PC (bins, or one column per label or tag; brief §4.6, change note §2) and one group per page on the phone (brief §4.5, change note §4c). The design asks for one order for both: dragging a column's grip, or Alt+←/→ on its header, sets the left-to-right order on the PC, the phone's View, filter and sort › Page order sets it on the phone, and each "syncs, and it also sets the phone's page order" (and the PC's column order). The order is per grouping: Bins, Labels and Tags each keep their own.

ADR 76 syncs saved views as the `views` part: each `TaskView` carries a `TaskViewConfig` (filters, Group by, sort, density, Pin highlights, the shuffle order), merged per view by its stamp, newest wins. A page order is not in it. The round 2 brief forbids syncing a new field before its ADR is accepted (§0.4), so the round 2 build keeps the order on each device (plan §3b item 3): the PC in its `tasks.layout` document (`TaskLayoutPreferences.ColumnOrder`, per grouping, never synced, a device setting in backups), the phone in its own preferences once R7 builds the paged Shelf. Until this ADR is accepted the PC's column order and the phone's page order are independent.

## Decision

The page order rides inside the view config, not in a new sync part:

- `TaskViewConfig` gains an optional `pageOrder`: an object with up to three lists of group keys, `bins`, `labels`, `tags` (the keys the Shelf already uses: `bin:sort`, `bin:next`, `bin:later`; `label:<label id>` and `label:none`; `tag:<lower-cased tag>` and `tag:none`). It holds no titles, label names or tag text beyond the tag key itself, which the view's own tag filter already carries; it is sealed with the rest of the sync file (ADR 22).
- A device applies a view's order for its current grouping: the named groups first, in that order; groups it doesn't name after them in the grouping's own order (a new label or tag lands at the end); names of groups that no longer exist are kept, so a label that comes back keeps its place. Moving a column or page stores the whole order shown, followed by the stored keys not shown now.
- Where it is stored: in the view in use (its working copy, as Group by and sort are). A moved column is a change to the view like any other: it shows as "changed" and Save changes stores it, as Save already does for sort and Group by. The default view saves at once, as it does today for its own changes.
- Merge: unchanged. The config is merged as one value per view by the view's stamp (ADR 76), so the newer device's order wins as a whole; two devices reordering the same view at once keep the newer order, never a mix.
- The phone's start page and wrap around stay on the phone (they are about the phone's paging, not the order), in its own preferences.
- Older builds: an old PC or phone ignores the unknown field when it reads a view and drops it when it writes the view back after an edit of its own, which loses the order only for that view (the next move puts it back). Nothing else changes for them.
- Migration: on the first run of a build with this ADR, the PC copies its device-local `tasks.layout` column orders into the default view's config if that view has none, then stops reading `ColumnOrder` for groupings the view names (the device-local value stays as the way back and for views without an order).

## Consequences

Moving a column on the PC reorders the phone's pages and the other way round, as the design asks, without a new sync part or a new merge rule. The cost is the view config's coarse merge: the whole order of a view is one value, and a view edited on an old build loses its order. The order is per view, so two views can order the same groups differently; the default view carries the everyday order. `doc/hard-rules.md` (the synced fields of ADR 76's `views` part) names `pageOrder` when it ships, and the round 2 PC build's device-local order (`tasks.layout`) becomes the fallback rather than the source.
