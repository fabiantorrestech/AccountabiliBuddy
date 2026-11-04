# 62. Let the newest task category colour win on every device

Date: 2026-11-04

## Status

Accepted

## Context

The owner recoloured a category (ePT, pink to blue) and past tasks kept the old colour. Tasks, Later items and come-backs store their category's name and colour, and some rows preferred the stored colour. Showing the category as it is now fixes that on one device, but it doesn't settle which device's colour is the current one.

Until now a recolour changed only that PC's list, and it reached other devices only through next-session tasks, which carry a colour. The PC takes the colour of a category's most recently edited next-session task. That edit can be about anything: a phone moving an old Later item to Today stamps a new edit with the item's old colour, which by this reasoning can turn the category back on the PC. We have not seen this happen; it comes from reading the code.

The owner also asked for a colour editor and a category list on the phone, which had neither (its picker listed categories seen on tasks, and its file sent no `categories`). With an editor on both sides and no way to compare edits, the phone's own colour would hide a newer one from the PC, which is the original complaint again.

## Decision

We will treat a category's colour as shared by name: the newest colour edit wins on every device. Each category gets an optional `colorEditedAt`, set when the user recolours it, in the device's own list and in the sealed sync file's `categories`. When another device's file arrives, a category of the same name (any case) takes a colour with a newer stamp. No category is created this way. A stamp counts at most 2 minutes ahead, the shared lists' rule, and a colour with no stamp is older than any stamped one.

Once a category has a `colorEditedAt`, the PC no longer takes its colour from next-session tasks. Comparing edit times there would not help, because moving an old Later item makes a new edit that still carries the old colour. A category with no stamp keeps the old behaviour until someone recolours it.

The phone keeps its own Keystore-sealed list of categories it created or edited and publishes that list in `categories`. It lists only those, so the PCs' categories don't come back to them under the phone's name. Renaming or deleting a category stays local to the device's own list, and relabels its shared items as ordinary stamped edits.

Colours stay `#RRGGBB`. We considered opacity and left it out, since every device checks that format and an 8 px dot gains nothing from it. `colorEditedAt` is optional and lives outside the profile document, so the profile schema does not change.

## Consequences

A recolour reaches every device of the group, and past rows, Later items and come-backs follow it. This replaces the rule that a recolour changes only that PC's list, so two PCs can no longer keep different colours for the same name.

Older apps ignore the new field and keep their old behaviour until updated, including taking colours from next-session tasks. Comparing edit times between devices trusts their clocks within the 2 minute limit, as the shared lists do. A rename or delete on one device doesn't change another device's own list, so that device still offers the old name; the phone's confirm text says so.
