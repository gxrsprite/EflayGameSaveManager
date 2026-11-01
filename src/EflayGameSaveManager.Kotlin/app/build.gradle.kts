plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
    id("org.jetbrains.kotlin.plugin.serialization")
    id("org.jetbrains.kotlin.plugin.compose")
}

val prepareSharedAssets by tasks.registering(Sync::class) {
    from("src/main/assets") { exclude("GameSaveManager.config.json") }
    from("../../EflayGameSaveManager.Maui/GameSaveManager.config.json")
    from("../../EflayGameSaveManager.Core/Assets/NfsMwBossReady") { into("nfs_mw_boss_ready") }
    into(layout.buildDirectory.dir("generated/sharedAssets"))
}

android {
    namespace = "com.eflay.gamesavemanager"
    compileSdk = 36

    defaultConfig {
        applicationId = "com.eflay.gamesavemanager"
        minSdk = 26
        targetSdk = 36
        versionCode = 1
        versionName = "1.0.0"
    }

    buildTypes {
        release {
            isMinifyEnabled = false
            proguardFiles(getDefaultProguardFile("proguard-android-optimize.txt"), "proguard-rules.pro")
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
        compose = true
    }
    sourceSets.getByName("main").assets.setSrcDirs(listOf(layout.buildDirectory.dir("generated/sharedAssets")))
}

tasks.named("preBuild").configure { dependsOn(prepareSharedAssets) }
tasks.withType<Test>().configureEach { systemProperty("nfsTemplatesDir", file("../../EflayGameSaveManager.Core/Assets/NfsMwBossReady").absolutePath) }

dependencies {
    // Compose BOM
    val composeBom = platform("androidx.compose:compose-bom:2024.12.01")
    implementation(composeBom)

    implementation("androidx.core:core-ktx:1.15.0")
    implementation("androidx.lifecycle:lifecycle-runtime-ktx:2.8.7")
    implementation("androidx.lifecycle:lifecycle-viewmodel-compose:2.8.7")
    implementation("androidx.activity:activity-compose:1.9.3")

    // Compose UI
    implementation("androidx.compose.ui:ui")
    implementation("androidx.compose.ui:ui-graphics")
    implementation("androidx.compose.ui:ui-tooling-preview")
    implementation("androidx.compose.material3:material3")
    implementation("androidx.compose.material:material-icons-extended")

    // Kotlin serialization
    implementation("org.jetbrains.kotlinx:kotlinx-serialization-json:1.7.3")

    // OkHttp for S3 HTTP
    implementation("com.squareup.okhttp3:okhttp:4.12.0")

    // Coroutines
    implementation("org.jetbrains.kotlinx:kotlinx-coroutines-android:1.9.0")

    // Shizuku for elevated file access (Maven Central)
    implementation("dev.rikka.shizuku:api:13.1.5")
    implementation("dev.rikka.shizuku:provider:13.1.5")
    testImplementation("junit:junit:4.13.2")

    // Debug
    debugImplementation("androidx.compose.ui:ui-tooling")
    debugImplementation("androidx.compose.ui:ui-test-manifest")
}
