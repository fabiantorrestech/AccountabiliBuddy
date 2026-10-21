plugins {
    alias(libs.plugins.android.application)
    alias(libs.plugins.kotlin.compose)
}

// The Pixel Watch companion (mockup, ADR 21 Proposed): rings for today's plan and focus, the running session and the
// week. Uses only libraries the phone app already pins; Wear-specific ones wait for the user's OK.
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
    implementation(libs.androidx.core.ktx)
    implementation(libs.androidx.activity.compose)
    implementation(platform(libs.androidx.compose.bom))
    implementation(libs.androidx.compose.ui)
    implementation(libs.androidx.compose.ui.graphics)
    implementation(libs.androidx.compose.material3)
    debugImplementation(libs.androidx.compose.ui.tooling)
}
