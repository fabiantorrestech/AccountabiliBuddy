package com.accountabilibuddy.shared.session

/** The app's mode, read live (S3 of the plan check): the rules for ending and locking differ by it (ADR 68, 85). */
enum class PhoneSessionMode { ACCOUNTABILI_BUDDY, PRODUCTA_BUDDY }
