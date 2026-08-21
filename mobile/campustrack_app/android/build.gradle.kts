allprojects {
    repositories {
        google()
        mavenCentral()
    }
}

// Flutter expects build output under the project's own build/ directory rather than each
// Gradle module's default location. `buildDir` was removed in Gradle 9, so this goes through
// the layout API.
val newBuildDir: Directory =
    rootProject.layout.buildDirectory
        .dir("../../build")
        .get()
rootProject.layout.buildDirectory.value(newBuildDir)

subprojects {
    val newSubprojectBuildDir: Directory = newBuildDir.dir(project.name)
    project.layout.buildDirectory.value(newSubprojectBuildDir)
}
subprojects {
    project.evaluationDependsOn(":app")
}

tasks.register<Delete>("clean") {
    delete(rootProject.layout.buildDirectory)
}
