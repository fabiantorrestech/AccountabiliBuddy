// Round 2 parity checks (plan §3b item 9): a plain JVM program, not part of any app. It compiles selected pure files of
// :shared by source path and runs them against JSON fixtures the C# checks write (the PC's inputs and outputs), printing
// PASS/FAIL. Run: ./gradlew :checks:run (fixtures from %TEMP%\claude\round2-fixtures, or --args="--fixtures <dir>").
// Nothing new is downloaded: Kotlin's JVM plugin comes from the Kotlin Gradle plugin already on the build classpath (AGP's
// built-in Kotlin and the Compose plugin), `application` is Gradle's own, and kotlinx.serialization.json is :shared's own.
plugins {
    kotlin("jvm")
    application
}

// Only pure files (no Android API) of :shared, listed one by one; add a file here when a check needs it. :shared itself is
// an Android library, so a JVM project can't depend on it.
val sharedSources = listOf(
    "com/accountabilibuddy/shared/sync/TaskPlannedFor.kt",
    "com/accountabilibuddy/shared/sync/TaskStepLetters.kt",
    "com/accountabilibuddy/shared/sync/TaskLabels.kt",
    // Round 2 R4.7: the goals model and maths (every file of the package is pure Kotlin + kotlinx.serialization.json).
    "com/accountabilibuddy/shared/goals/*.kt",
    // Round 2 R7.1: the task model, its field merge and the old task fields, with the sync file's JSON for the old fields: every
    // pure file of :shared's root and sync packages (sharedExcluded lists the ones that need Android).
    "com/accountabilibuddy/shared/tasks/*.kt",
    "com/accountabilibuddy/shared/sync/*.kt",
    "com/accountabilibuddy/shared/*.kt",
)

// Files of the packages above that use Android APIs (the Keystore, android.util.Base64), and the ones built on them.
val sharedExcluded = listOf(
    "com/accountabilibuddy/shared/TokenVault.kt",
    "com/accountabilibuddy/shared/sync/PinnedTrustManager.kt",
    "com/accountabilibuddy/shared/sync/SyncKey.kt",
    "com/accountabilibuddy/shared/sync/SyncFileCodec.kt",
    "com/accountabilibuddy/shared/sync/RekeyBuilder.kt",
    "com/accountabilibuddy/shared/sync/RekeyCodec.kt",
    "com/accountabilibuddy/shared/sync/RekeyWrap.kt",
)

java {
    sourceCompatibility = JavaVersion.VERSION_17
    targetCompatibility = JavaVersion.VERSION_17
}

// The listed files are copied into build/sharedSrc and compiled from there, so :shared's source folder is never a second content
// root of this module (Android Studio flags "duplicate content roots" otherwise).
val sharedCopy = tasks.register<Sync>("copySharedSources") {
    from("../shared/src/main/java") {
        include(sharedSources)
        exclude(sharedExcluded)
    }
    into(layout.buildDirectory.dir("sharedSrc"))
}

kotlin {
    compilerOptions {
        jvmTarget.set(org.jetbrains.kotlin.gradle.dsl.JvmTarget.JVM_17)
    }
    sourceSets.named("main") {
        kotlin.srcDir(sharedCopy)
    }
}

dependencies {
    implementation(libs.kotlinx.serialization.json)
}

// The JVM plugin adds Kotlin's scripting compiler plugin (for .kts sources) by default; this module has no scripts, and the
// artifact isn't otherwise part of the build, so it is left out rather than downloaded.
configurations.matching { it.name.startsWith("kotlinCompilerPluginClasspath") }.configureEach {
    exclude(group = "org.jetbrains.kotlin", module = "kotlin-scripting-compiler-embeddable")
}

application {
    mainClass.set("com.accountabilibuddy.checks.MainKt")
}
