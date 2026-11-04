# 63. Let day plan parts overlap, with the weaker rules winning

Date: 2026-11-04

## Status

Accepted

## Context

A day plan's parts could not overlap, except Pre-Work over Sleep (ADR 31) and a flexible break's window over the work around it. To open mail and chat for a while during work, the owner had to cut the Work part in two around a Comms part. The owner wants any category to be able to sit over any other, with the weaker rules applying in the time they share, and chose this for every category, Sleep and Pre-Work included, after being told that Rest over Pre-Work switches that guard off for that time.

The owner made that choice having been told overlaps would wait the looser-change delay, which was not true of the code. A plan change waited only if it freed today's guarded work time on a computer with the work-hours guard on (ADR 28, 47). Comms counts as work, so Comms over Work freed nothing and would apply at once, and Pre-Work isn't counted at all. ADR 18 had also said the day plan is never looser, because it only says when the buddy watches. Overlaps make that wrong: a part can now switch a guard off without removing anything.

## Decision

We will allow parts of any two categories to overlap. In the time they share, the lower rank wins: Rest 1, Comms 2, Work 3, Sleep 4, Pre-Work 5. This is the order `CalendarDayOverlay` already used for its base. On a tie the part that starts later keeps the shared time.

`DayPlan.PartsIn`, and the phone's `partsIn`, cut overlaps into pieces that don't overlap, so every guard and timeline gets one answer without changes of its own. Two pairs stay layered as they are today: a flexible break's window, which isn't a break until it is taken, and Pre-Work over Sleep, so an early start stays guarded (ADR 31). Weaker wins only between parts of one template. Parts from different templates keep "the later date wins", so existing plans don't change. Sleep times and alarms keep reading the Sleep part itself.

We will hold a change that weakens today. If any moment left today would drop to a lower rank than the plan in force gives it, the change waits the looser-change delay, with or without the work-hours guard, as a pending change with Cancel. This also applies to edits with no overlap, such as replacing Work with Comms today, which used to apply at once. The plan stays locked during a session the user started. This amends ADR 18, 28 and 31.

Calendar events are not part of this. When events overlap each other the stricter still wins, and an event never cuts Sleep or Pre-Work (ADR 58).

## Consequences

Comms over Work needs no cutting up, and the plan keeps the part it was laid over. Every moment of the day still has one answer, and PC and phone reach it by the same rule, which has to be kept the same in two languages.

A Rest part can now switch off Pre-Work's guard or the night's shading and bedtime reminder for its time, without the emergency unlock Pre-Work otherwise asks for. The delay and the visible pending change are what stand in the way, and that is a deliberate looser setting than before. More edits wait than used to, including ones with no overlap.

The data's shape doesn't change, so the schema stays at v13, but older devices refuse or misread a plan with overlaps, so the PC and the phone have to update together. Dragging no longer stops at a neighbour, so an edge snaps to it to make accidental overlaps less likely, and the editors have to draw overlapping parts side by side so a covered part can still be selected.
