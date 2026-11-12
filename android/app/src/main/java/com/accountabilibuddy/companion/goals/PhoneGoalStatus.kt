package com.accountabilibuddy.companion.goals

/** Whether the phone's goals can be used ([PhoneGoalStore]); the PC's `GoalModelState`. */
enum class PhoneGoalStatus {
    /** Nothing stored yet: the first run ([PhoneGoalsMigration]) hasn't made Work and Deep work. Changes are refused. */
    NotSetUp,

    /** Read and usable. */
    Ready,

    /**
     * Something is stored but can't be read (the Keystore key went with the app's data, a damaged or newer list, lists without
     * the marker). Never guessed at, rebuilt or written over: every change is refused and the screen says so.
     */
    Unreadable,
}
