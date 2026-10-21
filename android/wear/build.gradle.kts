plugins {
    alias(libs.plugins.android.application)
    alias(libs.plugins.kotlin.compose)
}

// The Pixel Watch companion (ADR 21): rings for today's plan and focus, the running session and the week, from the
// phone's snapshot. Compose for Wear OS, a Tile and a complication (libraries approved by the user on 2026-10-21).
android {
    namespace = "com.accountabilibuddy.watch"
    compileSdk = 37

    defaultConfig {
        // The phone app's id: Wear OS pairs a watch app with the phone app of the same id (needed once they talk).
        applicationId = "com.accountabilibuddy.companion"
        // Wear OS 3 and newer (every Pixel Watch).
        minSdk = 30
        targetSdk = 36
        versionCode = 150
        versionName = "0.1.50"
    }

    buildTypes {
        release {
            isMinifyEnabled = true
            isShrinkResources = true
            proguardFiles(getDefaultProguardFile("proguard-android-optimize.txt"))
            signingConfig = signingConfigs.getByName("debug")
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    buildFeatures {
        compose = true
    }
}

kotlin {
    compilerOptions {
        jvmTarget.set(org.jetbrains.kotlin.gradle.dsl.JvmTarget.JVM_17)
    }
}

dependencies {
    implementation(project(":shared"))
    // The phone link (ADR 21): Wearable Data Layer messages from the phone app.
    implementation(libs.play.services.wearable)
    implementation(libs.androidx.core.ktx)
    implementation(libs.androidx.activity.compose)
    implementation(platform(libs.androidx.compose.bom))
    implementation(libs.androidx.compose.ui)
    implementation(libs.androidx.compose.ui.graphics)
    // Compose for Wear OS: round screens, the pager, the time at the top.
    implementation(libs.wear.compose.material3)
    implementation(libs.wear.compose.foundation)
    debugImplementation(libs.androidx.compose.ui.tooling)
}
