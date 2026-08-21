pluginManagement {
    val flutterSdkPath =
        run {
            val properties = java.util.Properties()
            file("local.properties").inputStream().use { properties.load(it) }
            val flutterSdkPath = properties.getProperty("flutter.sdk")
            require(flutterSdkPath != null) { "flutter.sdk not set in local.properties" }
            flutterSdkPath
        }

    includeBuild("$flutterSdkPath/packages/flutter_tools/gradle")

    repositories {
        google()
        mavenCentral()
        gradlePluginPortal()
    }
}

// Versions follow the Flutter 3.47 template. They are not arbitrary: AGP 9 removed the
// `applicationVariants` API and Gradle 9 removed the `buildDir` property, so an older
// combination copied from a tutorial fails at configuration time rather than compiling
// into something subtly wrong.
plugins {
    id("dev.flutter.flutter-plugin-loader") version "1.0.0"
    id("com.android.application") version "9.1.0" apply false
    id("org.jetbrains.kotlin.android") version "2.4.0" apply false
    // Applied by app/build.gradle.kts only when google-services.json is present.
    id("com.google.gms.google-services") version "4.4.2" apply false
}

include(":app")
