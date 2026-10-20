package com.accountabilibuddy.companion.model

/** How a rule's pattern is compared; all comparisons ignore case. [Domain] matches a host and its subdomains. */
enum class MatchKind { Exact, Contains, Prefix, Regex, Domain }
