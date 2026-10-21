plugins {
    alias(libs.plugins.android.library)
}

// Code the phone app and the watch app share (ADR 21): today's focus as the PC sends it, and the watch snapshot.
// Plain Kotlin and kotlinx.serialization only, so the scratch checks can compile it on a plain JVM too.
android {
    namespace = "com.accountabilibuddy.shared"
    compileSdk = 37

    defaultConfig {
        minSdk = 26
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
}

kotlin {
    compilerOptions {
        jvmTarget.set(org.jetbrains.kotlin.gradle.dsl.JvmTarget.JVM_17)
    }
}

dependencies {
    // Part of this module's API (FocusDayJson reads and writes JsonObject).
    api(libs.kotlinx.serialization.json)
}
