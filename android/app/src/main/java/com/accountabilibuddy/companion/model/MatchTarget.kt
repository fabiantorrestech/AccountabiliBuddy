package com.accountabilibuddy.companion.model

/** Which part of the observed activity a rule is matched against (same names as the Windows app, ADR 7). */
enum class MatchTarget { ProcessName, WindowTitle, Url, OcrText, Channel }
