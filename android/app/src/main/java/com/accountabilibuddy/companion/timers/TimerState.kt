package com.accountabilibuddy.companion.timers

/** Where a timer is: set but not started, counting down, paused, or up (ringing until dismissed). */
enum class TimerState { IDLE, RUNNING, PAUSED, DONE }
