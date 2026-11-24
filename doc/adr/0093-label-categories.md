# 93. Label categories

Date: 2026-11-24

## Status

Accepted (owner, 2026-11-24; Tasks redesign D4: labels get categories with an icon; categories are always shared; goals keep counting single labels; prefixes become suggested categories the user confirms; the release waits until the phone understands categories).

Amends [76. One task list with bins, labels and tags](0076-one-task-list-with-bins-labels-and-tags.md) (a label gains a category with a stamp of its own, and a new optional sync part carries the categories) and [89. Device-only labels and tags that sync but stay hidden](0089-device-only-labels-and-tags-that-sync-but-stay-hidden.md) (a category is never device-only).

## Context

The Tasks redesign puts the labels in one row and groups them, everywhere they are listed or picked, under categories such as Work, AB and Personal, each with an icon. Today a label is a flat record (`TaskLabel`: id from its name, name, colour, position, rings on, only on) that merges as a whole, newest edit wins, with "rings on" and "only on" merging on stamps of their own so an older app's whole-label edit can't take them back. Users already fake categories with name prefixes ("Work - AI", "AB-Improvements").

Constraints: the code and the old sync field already use "category" for what the UI now calls a label (ADR 62 `TaskCategory`, the `categories` field), so the new thing needs another name in code. The sync file's `formatVersion` makes an older reader refuse the whole file, so it can't be raised for an additive change. The phone app is updated after the PC, so for a while an older phone edits labels without knowing categories. The PC and the phone draw icons from different Material libraries (ADR 32).

## Decision

We will add label categories, called **label groups** in code and in the sync files and "categories" only in the UI.

A group (`TaskLabelGroup`) has an id, a name, an icon key, a position, an edit time and, once deleted, a deletion time. Its id is made from its trimmed, lower-cased name the way a label's is (a version-5 style id in a namespace of its own), so two devices that make "Work" offline make one group. The icon is a neutral key from a list both apps map to their own Material icon; an unknown key shows a default icon. Groups travel in one new optional part of the sealed sync file, `labelGroups`, appended after the last part, read leniently, merged item by item (newest edit wins, `SharedListMerge`), under "Sync task lists" like the other task parts: never in a QR file, never in the copy a receiver keeps of another device's file, never sent on to the watch, never logged; receipts count it with the "tasks" part. Group tombstones keep only ids, position and stamps and are kept for good, like label tombstones. The format version stays 1; an older app ignores the part.

A label gains two optional fields, `groupId` and `groupStamp` (when and on which device the group was chosen; a stamp with no id is a choice of "no category"). They merge on their own, as `onlyOn` does (`TaskLabelMerge`): the newest stamp wins, a stamped choice beats an unstamped one, then the device id, then the value; a stamp over 2 minutes ahead counts as 2 minutes ahead. So an older phone's whole-label edit, which knows nothing of groups, never takes a category away. A label whose group is missing or deleted shows as uncategorised; nothing rewrites the label for it.

A group is always shared: it has no device list, and a device-only label inside a shared group is fine. Goals keep counting single labels. A saved view's label filter that the user fills from a whole category stores the category's label ids at the time of the pick; views gain no field. Group by Labels orders its columns by group position, then label position, with uncategorised labels last.

On upgrade nothing is grouped automatically. A setup assistant page (SetupFeatures, also offered from the migration assistant) suggests groups from shared name prefixes (`^\s*([^-]+?)\s*-\s*(.+)$` shared by two or more labels) and creates only those the user ticks; "Shorten the names" is an opt-in that renames each label to the part after the prefix, skipping any name that would clash with another label.

## Consequences

Category choices survive older apps' label edits, and two devices that make the same category offline end up with one. The release that brings categories waits until the phone reads and writes `labelGroups` (owner, 2026-11-24); until then a dev build that syncs with an older phone shows categories on the PC only, and the phone shows flat labels. The phone's Kotlin model needs the same group record, the part and the stamped merge, checked for parity against the PC.

A renamed group changes its name but keeps its id, so a device that later makes a group with the old name gets a second group; the same is true of labels today. Shortened names are label renames, which older phones read by name (ADR 76 keeps them from bringing the old name back), so shortening stays opt-in. Task-list backups and exports carry the groups as `labelGroups`.
