plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
}

android {
    namespace = "org.librestream.app"
    compileSdk = 34

    defaultConfig {
        applicationId = "org.librestream.app"
        minSdk = 24
        targetSdk = 34
        versionCode = 1
        versionName = "0.5.0"
    }

    buildTypes {
        release {
            isMinifyEnabled = false
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
    kotlinOptions {
        jvmTarget = "17"
    }
    buildFeatures {
        buildConfig = true
    }

    // La interfaz compartida (index.html + eq.js) se genera con `node app/build.mjs` y se empaqueta como assets
    sourceSets["main"].assets.srcDir("../../app/dist")
}

dependencies {
    implementation("androidx.webkit:webkit:1.12.1")
    testImplementation("junit:junit:4.13.2")
}
