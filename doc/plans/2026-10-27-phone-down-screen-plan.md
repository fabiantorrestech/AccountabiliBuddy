# "Phone down" unlock screen — Plan (user, 2026-10-27)

**Goal:** during a PC focus session with the phone placement guard on, unlocking the phone shows a "Phone down" screen instead of the Essentials screen: "Phones down: in a bag or somewhere off you". It extends phase 11b (phone placement) and A 2 (Essentials).

## Behaviour
- **When it shows:** the phone is unlocked during a real focus session (not a test, break, lunch, work use or emergency) and the placement guard is on.
- **Its information:** the session's time left, the pickups so far, and a one-line reminder.
- **Firm like Essentials:**
  - Back does nothing.
  - Home brings it back in about 3 s unless an option was picked.
  - It never shows over a call.
  - **Incoming calls are whitelisted:** a ringing or active call is never covered or blocked, so the user needs to do nothing.
- **Options:**
  1. **Putting it away** (main button): closes the screen. The display goes off when the user locks it, and the placement guard confirms bag / face down.
  2. **Need it for work:** the existing work use (Call / 2FA / Work app / Other + note + 5/10/15 min; from the 4th use, a 20-character note and a 30 s wait).
  3. **Essentials / quick reply:** opens the Essentials screen (Phone, Messages, Maps, Wallet…); counted as a pickup.
  4. **Emergency:** the existing emergency unlock (reason + minutes, daily limit).

## Pocket penalties (each on by default, each can be switched off)
- **Unlock friction:** while the placement guard reads `pocket`, the screen waits 10 s before its options appear. A bag or desk phone gets no wait.
- **No phone-free credit:** a session with more than about 5 minutes in a pocket doesn't count as "phone-free", so it breaks the phone-free streak (the PC's `PhonePickups` summary). The session review shows "Pocket N min".
- **Turning either off** is a looser change: it waits, and it's locked during a session (the same rule as the placement switch). Turning one on applies at once.

## Notes
- The phone's prefs ("placement" / "phoneDown"), never synced except the existing `placement` fields. The PC reads `pocketMinutes` for the phone-free rule.
- Record in `doc/hard-rules.md` after merge. SetupFeatures entry (phone 0.1.63).
- Build order: after the current batch's shared tasks Task 3 (both edit the phone's Today / CompanionApp).
