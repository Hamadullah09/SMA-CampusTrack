import java.util.Properties

plugins {
    id("com.android.application")
    // The Flutter Gradle Plugin must be applied after the Android and Kotlin Gradle plugins.
    id("dev.flutter.flutter-gradle-plugin")
}

/**
 * Release signing.
 *
 * The keystore and its passwords never live in this repository. They come from
 * key.properties (gitignored, see key.properties.template) for a local release build, or
 * from environment variables in CI, where they arrive as repository secrets.
 *
 * If neither is present the release build falls back to the debug key so that
 * `flutter build apk --release` still works for a quick test install. Such a build must
 * never reach families: Android identifies an app by its signature, so a debug-signed
 * install cannot be upgraded in place by a properly signed one later. The release workflow
 * runs apksigner and fails rather than publishing one.
 */
val keystoreProperties = Properties()
val keystorePropertiesFile = rootProject.file("key.properties")
if (keystorePropertiesFile.exists()) {
    keystorePropertiesFile.inputStream().use { keystoreProperties.load(it) }
}

val keystorePathFromEnv: String? = System.getenv("ANDROID_KEYSTORE_PATH")
val hasReleaseKey = keystorePropertiesFile.exists() || !keystorePathFromEnv.isNullOrEmpty()

android {
    namespace = "net.smatechno.campustrack"

    // Follows the Flutter SDK rather than pinning numbers that go stale: 36 / 24 / 36 as of
    // Flutter 3.47, which already exceeds the API 23 that local_auth's biometric prompt needs.
    compileSdk = flutter.compileSdkVersion
    ndkVersion = flutter.ndkVersion

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17

        // flutter_local_notifications uses java.time APIs that do not exist below API 26.
        // Desugaring back-fills them; without this the build fails outright.
        isCoreLibraryDesugaringEnabled = true
    }

    defaultConfig {
        // Permanent. Android identifies the app by this string and it can never change.
        // Derived from sma-techno.net; the hyphen is dropped because a Java package segment
        // cannot contain one.
        applicationId = "net.smatechno.campustrack"

        minSdk = flutter.minSdkVersion
        targetSdk = flutter.targetSdkVersion

        versionCode = flutter.versionCode
        versionName = flutter.versionName
    }

    signingConfigs {
        create("release") {
            // PKCS12 by default: that is what keytool has produced since JDK 9 and what
            // tools/generate-release-key.py writes. An older JKS still works by setting
            // storeType=JKS in key.properties.
            if (keystorePropertiesFile.exists()) {
                storeFile = file(keystoreProperties.getProperty("storeFile"))
                storeType = keystoreProperties.getProperty("storeType", "PKCS12")
                storePassword = keystoreProperties.getProperty("storePassword")
                keyAlias = keystoreProperties.getProperty("keyAlias")
                keyPassword = keystoreProperties.getProperty("keyPassword")
            } else if (!keystorePathFromEnv.isNullOrEmpty()) {
                storeFile = file(keystorePathFromEnv)
                storeType = System.getenv("ANDROID_KEYSTORE_TYPE") ?: "PKCS12"
                storePassword = System.getenv("ANDROID_KEYSTORE_PASSWORD")
                keyAlias = System.getenv("ANDROID_KEY_ALIAS")
                keyPassword = System.getenv("ANDROID_KEY_PASSWORD")
            }
        }
    }

    buildTypes {
        release {
            signingConfig =
                if (hasReleaseKey) {
                    signingConfigs.getByName("release")
                } else {
                    signingConfigs.getByName("debug")
                }

            // Shrinking is off deliberately. The saving is modest for an app this size, and
            // an over-eager rule stripping a Firebase or Riverpod class produces a crash that
            // only appears on a real device after release. Turn it on with a proguard file
            // once there is a device farm to catch that.
            isMinifyEnabled = false
            isShrinkResources = false
        }
    }
}

kotlin {
    compilerOptions {
        jvmTarget = org.jetbrains.kotlin.gradle.dsl.JvmTarget.JVM_17
    }
}

flutter {
    source = "../.."
}

dependencies {
    coreLibraryDesugaring("com.android.tools:desugar_jdk_libs:2.1.5")
}

/**
 * Firebase is optional. Applying the google-services plugin without google-services.json
 * fails the build, so push notifications light up when the file is added rather than
 * blocking every build until then.
 */
if (file("google-services.json").exists()) {
    apply(plugin = "com.google.gms.google-services")
} else {
    logger.lifecycle("google-services.json not found; building without Firebase push notifications.")
}
