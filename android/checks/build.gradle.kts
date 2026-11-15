// LockMyPhone-Extended lock checks (lmp-lock review fixes S1-S6): a plain JVM program over the pure files of the phone app's
// essentials package (no Android API), printing PASS/FAIL. Run: ./gradlew :checks:run.
// NOTE for the merge into ui-round2: that branch has its own android/checks module (same name, settings include and CheckReport.kt).
// Take ui-round2's build.gradle.kts, add the files listed below to its `from("../app/src/main/java")` include list, add
// LmpLockSelfCheck.kt as it is and call lmpLockSelfCheck(report) from its selfChecks; drop LmpLockMain.kt.
plugins {
    kotlin("jvm")
    application
}

java {
    sourceCompatibility = JavaVersion.VERSION_17
    targetCompatibility = JavaVersion.VERSION_17
}

val appCopy = tasks.register<Sync>("copyAppSources") {
    from("../app/src/main/java") {
        include(
            "com/accountabilibuddy/companion/essentials/LooserChangeRules.kt",
            "com/accountabilibuddy/companion/essentials/HoldRules.kt",
            "com/accountabilibuddy/companion/essentials/CapabilityMemory.kt",
            "com/accountabilibuddy/companion/essentials/RedirectRules.kt",
            "com/accountabilibuddy/companion/essentials/SigningCertificates.kt",
        )
    }
    into(layout.buildDirectory.dir("appSrc"))
}

kotlin {
    compilerOptions {
        jvmTarget.set(org.jetbrains.kotlin.gradle.dsl.JvmTarget.JVM_17)
    }
    sourceSets.named("main") {
        kotlin.srcDir(appCopy)
    }
}

configurations.matching { it.name.startsWith("kotlinCompilerPluginClasspath") }.configureEach {
    exclude(group = "org.jetbrains.kotlin", module = "kotlin-scripting-compiler-embeddable")
}

application {
    mainClass.set("com.accountabilibuddy.checks.LmpLockMainKt")
}
