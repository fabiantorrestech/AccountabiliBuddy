package com.accountabilibuddy.shared.goals

/**
 * The banner under the chart: [headline] ("2 days behind your target"), [body] and how it reads ([tone]: amber late, teal ahead,
 * neutral for a pace with no date to compare).
 */
data class GoalTrendBanner(val headline: String?, val body: String, val tone: Tone) {
    enum class Tone { Amber, Teal, Neutral }
}
