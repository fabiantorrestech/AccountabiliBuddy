pluginManagement {
    repositories {
        google()
        mavenCentral()
        gradlePluginPortal()
    }
}

dependencyResolutionManagement {
    repositoriesMode.set(RepositoriesMode.FAIL_ON_PROJECT_REPOS)
    repositories {
        google()
        mavenCentral()
    }
}

rootProject.name = "AccountabiliBuddy"
include(":app")
// Code the phone and watch apps share (ADR 21).
include(":shared")
// The Pixel Watch companion (ADR 21, Proposed; mockup on sample data).
include(":wear")
// Round 2 parity checks: a JVM program over pure :shared files and the C# checks' JSON fixtures (./gradlew :checks:run).
include(":checks")
