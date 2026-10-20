package com.accountabilibuddy.companion.data

/** A file that can't be imported, with a message for the user. */
class ImportException(message: String, cause: Throwable? = null) : Exception(message, cause)
