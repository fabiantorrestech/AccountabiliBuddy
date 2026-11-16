package com.accountabilibuddy.checks

import com.accountabilibuddy.companion.sync.DefaultDeviceName
import java.io.File

/**
 * CP1a: AB's own debug build installs next to the release build. The pure rule (the default device name) and source scans of
 * the build files, manifests and string resources that make it so (a scan reads the files the build reads, so a change to them is caught here).
 */
object Cp1aSelfCheck {
    private const val RELEASE_ID = "com.accountabilibuddy.companion"
    private val ANDROID_ROOTS = listOf("", "../", "../../")

    private fun file(path: String): String? = ANDROID_ROOTS.map { File(it + path) }.firstOrNull { it.isFile }?.readText()?.replace("\r\n", "\n")

    /** The code of a Gradle file: comment lines left out. */
    private fun code(text: String): String = text.lines().filterNot { it.trim().startsWith("//") || it.trim().startsWith("*") || it.trim().startsWith("/*") }.joinToString("\n")

    /** The text of `name { ... }` inside [text] (brace-balanced), or null. */
    private fun block(text: String, name: String): String? {
        val start = Regex("""\b$name\s*\{""").find(text) ?: return null
        var depth = 0
        for (i in start.range.last until text.length) {
            if (text[i] == '{') depth++ else if (text[i] == '}') { depth--; if (depth == 0) return text.substring(start.range.last, i) }
        }
        return null
    }

    fun run(report: CheckReport) {
        deviceName(report)
        gradle(report)
        mainManifest(report)
        debugManifest(report)
        strings(report)
        adbLines(report)
    }

    private fun deviceName(report: CheckReport) {
        fun name(system: String?, pkg: String) = DefaultDeviceName.of(system, "Pixel 7", pkg)
        report.check("cp1a/name: the release build keeps the system name", name("Fab's Pixel", RELEASE_ID) == "Fab's Pixel")
        report.check("cp1a/name: the release build without a system name uses the model", name(null, RELEASE_ID) == "Pixel 7" && name("  ", RELEASE_ID) == "Pixel 7")
        report.check("cp1a/name: the debug build adds ' (debug)'", name("Fab's Pixel", "$RELEASE_ID.debug") == "Fab's Pixel (debug)")
        report.check("cp1a/name: the debug build adds it to the model too", name(null, "$RELEASE_ID.debug") == "Pixel 7 (debug)")
        report.check("cp1a/name: only an id that ends in .debug counts", name("X", "$RELEASE_ID.debugger") == "X" && name("X", "com.other.debug.app") == "X")
    }

    private fun gradle(report: CheckReport) {
        val app = file("app/build.gradle.kts")?.let(::code)
        val wear = file("wear/build.gradle.kts")?.let(::code)
        report.check("cp1a/gradle: both build files were found", app != null && wear != null)
        if (app == null || wear == null) return
        for ((label, text) in listOf("app" to app, "wear" to wear)) {
            val debug = block(text, "debug")
            val release = block(text, "release")
            report.check("cp1a/gradle ($label): debug has applicationIdSuffix \".debug\"", debug != null && Regex("""applicationIdSuffix\s*=\s*"\.debug"""").containsMatchIn(debug))
            report.check("cp1a/gradle ($label): release has no applicationIdSuffix", release != null && !release.contains("applicationIdSuffix"))
            report.check("cp1a/gradle ($label): no versionNameSuffix anywhere (B7: the version parsers need x.y.z)", !text.contains("versionNameSuffix"))
            report.check("cp1a/gradle ($label): the version name is plain x.y.z", Regex("""versionName\s*=\s*"\d+\.\d+\.\d+"""").containsMatchIn(text))
        }
        fun value(text: String, key: String) = Regex("""$key\s*=\s*"?([^"\n]+)"?""").find(text)?.groupValues?.get(1)?.trim()
        report.check("cp1a/gradle: phone and watch share the base id, version name and code",
            value(app, "applicationId") == RELEASE_ID && value(wear, "applicationId") == RELEASE_ID &&
                value(app, "versionName") == value(wear, "versionName") && value(app, "versionCode") == value(wear, "versionCode"))
    }

    private fun mainManifest(report: CheckReport) {
        val manifest = file("app/src/main/AndroidManifest.xml")
        report.check("cp1a/manifest: the main manifest was found", manifest != null)
        if (manifest == null) return
        val affinities = Regex("""android:taskAffinity="([^"]*)"""").findAll(manifest).map { it.groupValues[1] }.toList()
        val prefix = "\${applicationId}."
        report.check("cp1a/manifest: taskAffinity values exist (7 today)", affinities.size >= 7, "found ${affinities.size}")
        report.check("cp1a/manifest: every taskAffinity is applicationId-relative, never the release id as a literal",
            affinities.isNotEmpty() && affinities.all { it.startsWith(prefix) && it.length > prefix.length }, affinities.joinToString())
    }

    private fun debugManifest(report: CheckReport) {
        val manifest = file("app/src/debug/AndroidManifest.xml")
        report.check("cp1a/debug manifest: found", manifest != null)
        if (manifest == null) return
        val elements = Regex("""<(receiver|activity)\b[^>]*>""").findAll(manifest).map { it.value }.toList()
        val exported = elements.filter { it.contains("android:exported=\"true\"") }
        // DebugVoiceActivity is disabled until the (guarded) VOICE receiver turns it on, and must stay open to the speech intent.
        val guarded = exported.filterNot { it.contains(".debug.DebugVoiceActivity") }
        report.check("cp1a/debug manifest: the four receivers and the gallery are exported (5)", guarded.size == 5, "found ${guarded.size}")
        report.check("cp1a/debug manifest: each of them needs android.permission.DUMP (adb shell only)",
            guarded.isNotEmpty() && guarded.all { it.contains("android:permission=\"android.permission.DUMP\"") })
        report.check("cp1a/debug manifest: the voice stand-in stays disabled by default",
            elements.firstOrNull { it.contains(".debug.DebugVoiceActivity") }?.contains("android:enabled=\"false\"") == true)
    }

    private fun strings(report: CheckReport) {
        fun names(text: String?) = text?.let { t -> Regex("""<string name="(\w+)">([^<]*)</string>""").findAll(t).associate { it.groupValues[1] to it.groupValues[2] } }.orEmpty()
        val appMain = names(file("app/src/main/res/values/strings.xml"))
        val appDebug = names(file("app/src/debug/res/values/strings.xml"))
        val wearMain = names(file("wear/src/main/res/values/strings.xml"))
        val wearDebug = names(file("wear/src/debug/res/values/strings.xml"))
        report.check("cp1a/strings: the release app is called AccountabiliBuddy, the debug app AB Debug",
            appMain["app_name"] == "AccountabiliBuddy" && appDebug["app_name"] == "AB Debug")
        val wearManifest = file("wear/src/main/AndroidManifest.xml").orEmpty()
        val labels = Regex("""android:label="@string/(\w+)"""").findAll(wearManifest).map { it.groupValues[1] }.toSet()
        report.check("cp1a/strings: the watch manifest has the app, tile and two complication labels",
            labels.containsAll(listOf("app_name", "tile_label", "complication_label", "timers_complication_label")), labels.joinToString())
        val missing = labels.filterNot { wearDebug[it] != null && wearDebug[it] != wearMain[it] }
        report.check("cp1a/strings: every watch label (app, tile, complications) is overridden in debug and differs from release", labels.isNotEmpty() && missing.isEmpty(), missing.joinToString())
    }

    private fun adbLines(report: CheckReport) {
        val debugDir = ANDROID_ROOTS.map { File(it + "app/src/debug/java") }.firstOrNull { it.isDirectory }
        val text = debugDir?.walkTopDown()?.filter { it.isFile && it.extension == "kt" }?.joinToString("\n") { it.readText() }?.replace("\r\n", "\n")
        report.check("cp1a/adb: the debug sources were found", text != null)
        if (text == null) return
        // A KDoc line may wrap after "-p"; join it so the package on the next line is seen.
        val joined = text.replace(Regex("""-p\s*\n\s*\*\s*"""), "-p ")
        report.check("cp1a/adb: no KDoc line targets the release id with -p", !Regex("""-p\s+${Regex.escape(RELEASE_ID)}(?![.\w])""").containsMatchIn(joined))
        report.check("cp1a/adb: no KDoc line starts a component through the release id with -n", !Regex("""-n\s+${Regex.escape(RELEASE_ID)}/""").containsMatchIn(joined))
        report.check("cp1a/adb: the gallery is started with the full class name", joined.contains("-n $RELEASE_ID.debug/$RELEASE_ID.debug.KitGalleryActivity"))
    }
}
