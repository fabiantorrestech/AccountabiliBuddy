# 73. Use LockMyPhone-Extended as the phone's lock when it is installed

Date: 2026-11-09

## Status

Accepted

## Context

The phone has two ways to keep the user to their essentials.

**The built-in essentials screen** (`EssentialsActivity`) is soft. Back does nothing, Home brings it back, and nothing is blocked. It comes up:
- on unlocking during a session on another device;
- at night, from bedtime until the wake time;
- during emergency phone time and "Need it for work";
- after 3 minutes out of place;
- from the work card's and the Phone down screen's Essentials buttons.

**LockMyPhone-Extended (LMP)**, the owner's own app, really locks the phone. AB starts one of its profiles through its public Intent API, but only on unlocking in a session and at bedtime. One "Use LockMyPhone-Extended" switch covers both, and the profile can be any LMP profile.

As a result the two overlap: at night the built-in bedtime screen and LMP's night lock can both run. And LMP's profiles can let the user out of a session through early unlock, NFC unlock or a Limited style, against the hard rule that a session the user started has no escape hatches.

On 2026-11-09 the owner decided:
- LMP is the phone's lock whenever it is installed, and the built-in screen is the fallback;
- LMP owns how a lock behaves, and AB owns what is on the screen of the profiles it manages;
- emergency phone time and "Need it for work" should leave the phone free rather than land on either screen.

The matching changes on LMP's side are its ADRs 0011 (companion hold), 0012 (screen options and a shared permission) and 0013 (morning lock before the first alarm).

## Decision

We will replace the "Use LockMyPhone-Extended" switch with one global choice, **How your phone locks**: LockMyPhone-Extended or Built-in Essentials.
- **Default:** LMP when it is installed, the built-in screen otherwise. The built-in choice explains that LMP gives more control and offers it (ADR 74).
- **Switching from LMP to the built-in screen** is a looser change: it waits the looser-change delay and never applies during a session. Switching to LMP applies at once.
- **Each list** (Focus, Bedtime) keeps its own profile choice. The default is the profile AB manages for that list, which AB creates in LMP when it is missing, even if the user has other profiles. The user may pick one of their own instead.

With LMP chosen, the built-in screen is never shown. Every place that showed it during a session asks LMP to lock instead:
- unlocking;
- the work card's and the Phone down screen's buttons, which then say they start LockMyPhone-Extended;
- 3 minutes out of place.

The night uses either the night lock or the bedtime screen, never both.

AB asks through LMP's companion hold (its ADR 0011). The hold runs until the session's end and moves when the end moves. A session with no set end gets a 30-minute lease that AB renews. While the hold is on, the lock runs strict. AB reads LMP's replies:
- if LMP refuses, or if LMP is missing, too old or without the permission, AB falls back to the built-in screen and says so in Settings and on Today;
- if a stronger lock of the user's runs under the hold, AB shows that lock's name.

The token-based `STOP_LOCK` and the plain `START_LOCK` stay only for an LMP without the hold.

Emergency phone time and "Need it for work" leave the phone free in both modes. No screen comes up. AB releases the hold it holds for those minutes, the way lunch lifts it today, and asks again when they end.

AB sends every option of its essentials screen to its managed profiles: apps, hidden, folders, order, some-screens, the Phone button and icons (LMP's hidden-app search already works like AB's). AB declares LMP's `MANAGE_PROFILES` permission itself (LMP ADR 0012), so the two link in either install order.

The phone's setup gains two pages, "How your phone locks" and "When it engages", replacing "The night":
- **Occasions:** during focus sessions, at night, and in the morning. Each can be switched off.
- **Starter apps:** each occasion gets a starter list taken from this phone's default apps (dialer, messages, clock, calendar, maps, an authenticator).
- **Morning:** this occasion needs LMP. It sends LMP's morning lock (first pickup, or a set time before the first alarm) through LMP's `SET_MORNING_LOCK` (its ADR 0013).
- **Existing installs:** these are offered the pages through SetupFeatures. Starters only ever add to a list there.

## Consequences

The phone has one lock in charge at a time, and the strongest available one is used by default. A session can't be escaped through LMP's own unlock options. The user sets their essentials once, in AB, and they apply to LMP's lock in full.

Most of the phone's session enforcement now rests on another app. A missing or broken LMP falls back to the softer built-in screen, which is weaker than today's behavior for anyone relying on LMP's lock. That is why the fallback is shown, not silent.

Emergency phone time and "Need it for work" become looser than before. They no longer keep the user to the essentials during their minutes. They stay limited in number and length as they are today.

The hold, the `LOCK_RESULT` reply and the capabilities check are a contract between two apps that must keep the same signing key. Reversing this means restoring the per-entry built-in screen and the switch, which is cheap in AB. The cost sits in LMP's contract, which other versions of AB will keep relying on.
