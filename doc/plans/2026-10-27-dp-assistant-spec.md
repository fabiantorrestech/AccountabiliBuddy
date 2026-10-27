# Setup DP Assistant: spec (user, 2026-10-27; build after release 0.1.111)
A button in the PC Day plan tab, top right next to the refresh button: **"Setup DP Assistant"**. It re-runs a guided day-plan assistant with sensible defaults, each step prefilled. It saves through `ConfigurationService` (looser waits; locked during a session) and reuses the "Your workdays" step.

1. **Workdays:** any days can be picked; Mon–Fri are ticked by default.
2. **Work time:** fixed hours (a work window, default 9:00 AM–5:00 PM) or a Budget of hours.
   - The page says this is how long you're at work, or the **window** you try to stay on task in.
   - It is not how many of those hours are focused work.
3. **Deep work goal:** "How many deep work / focus hours should we aim for?"
   - Default **1 h**, which sets the daily goal.
   - Guidance on the page: if you're not used to deep work, start low. Cal Newport notes most professionals top out around 4–4.5 h a day of deep, non-context-switching work, and only after a lot of deliberate practice.
4. **Lunch:** a lunch **window** (flexible, as today), or a **fixed lunch period**.
   - **Fixed period:** the lunch timer starts automatically at that time.
   - **A session that runs into it** (overlaps, or ends within 1 minute of it): the user is asked at scheduling time, and again with a popup 5 minutes before lunch, to choose one of:
     - **Keep:** confirm scheduling up against the lunch block;
     - **Adjust:** shorten the focus block to end before lunch;
     - **Flexible this time:** "you must click Start lunch in the app before you go".
5. **Sleep:** the same options as the bedtime setup (bedtime, wake from the alarm, smart bedtime…).
   - It can be skipped, with a note that sleep features won't work then.
6. **Weeks:** the same every week, alternating A/B, or a custom repeating cycle of N weeks (Week 1…Week N, then repeat, up to **8 weeks**).
   - This extends the v12 week map from A/B to N weeks. Needs a schema bump (v13) with a migration and an ADR 47 amendment.
7. **Review:** bullet points of how AccountabiliBuddy will behave through each day with these settings. For example:
   - "Before 9:00 AM: Pre-Work…"
   - "9:00 AM–5:00 PM: work hours guarded until 1 h of focus…"
   - "12:00 PM: lunch starts automatically…"
   - "After 5:00 PM: stays out of your way…"
   - "10:30 PM: bedtime reminder…"
   - Then "Save" or "Back".

Plan doc first (`doc/plans/`, with the N-week schema change and the fixed-lunch prompts on PC and phone), then build and review.
