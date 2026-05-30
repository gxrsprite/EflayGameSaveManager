package com.eflay.gamesavemanager.service

import android.util.Log
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import rikka.shizuku.Shizuku
import java.io.BufferedReader
import java.io.File
import java.io.InputStreamReader

/**
 * Provides elevated file access via Shizuku for paths normally inaccessible
 * to apps (e.g. /data/data/other.package/).
 *
 * Uses Shizuku.newProcess() to run shell commands with ADB/root-level
 * privileges. Requires Shizuku or Sui to be installed and authorized.
 *
 * Note: Shell (ADB) identity (UID 2000) cannot access /data/data/ directly.
 * Root (UID 0) is required for other app private directories. Shizuku
 * started via ADB provides UID 2000; Sui (Magisk) provides UID 0.
 */
object ShizukuHelper {

    private const val TAG = "ShizukuHelper"

    // ---- availability ----

    fun isInstalled(): Boolean {
        return try {
            Shizuku.pingBinder()
        } catch (_: Exception) {
            false
        }
    }

    fun hasPermission(): Boolean {
        return try {
            Shizuku.checkSelfPermission() == android.content.pm.PackageManager.PERMISSION_GRANTED
        } catch (_: Exception) {
            false
        }
    }

    fun isAvailable(): Boolean = isInstalled() && hasPermission()

    /** Returns the UID under which Shizuku commands run (0 = root, 2000 = adb). */
    fun getUid(): Int {
        return try {
            Shizuku.getUid()
        } catch (_: Exception) {
            -1
        }
    }

    /** True if running as root — required for /data/data/ access. */
    fun isRoot(): Boolean = getUid() == 0

    // ---- file ops via Shizuku shell ----

    /**
     * Copies a restricted file or directory to an accessible temporary location.
     * Returns the temp File on success, null on failure.
     */
    suspend fun copyFromRestricted(restrictedPath: String, destDir: File): File? =
        withContext(Dispatchers.IO) {
            if (!isAvailable()) return@withContext null
            if (!isRestrictedPath(restrictedPath)) return@withContext null

            val src = escapeShell(restrictedPath)
            destDir.mkdirs()

            val destName = restrictedPath.trimEnd('/').substringAfterLast('/').ifBlank { "copy" }
            val tempDest = File(destDir, "shizuku-$destName")
            tempDest.deleteRecursively()

            val srcFile = File(restrictedPath)
            val cmd = if (srcFile.isDirectory || restrictedPath.endsWith("/"))
                "cp -rT $src ${escapeShell(tempDest.absolutePath)}"
            else
                "cp $src ${escapeShell(tempDest.absolutePath)}"

            Log.d(TAG, "copyFromRestricted: $cmd")
            val result = runShell(cmd)
            if (result.success && tempDest.exists()) tempDest else null
        }

    /**
     * Copies accessible content into a restricted path.
     */
    suspend fun copyToRestricted(sourceDir: File, restrictedPath: String): Boolean =
        withContext(Dispatchers.IO) {
            if (!isAvailable()) return@withContext false
            if (!isRestrictedPath(restrictedPath)) return@withContext false

            val parent = File(restrictedPath).parentFile
            if (parent != null) {
                runShell("mkdir -p ${escapeShell(parent.absolutePath)}")
            }

            val cmd = if (sourceDir.isDirectory)
                "cp -rT ${escapeShell(sourceDir.absolutePath)} ${escapeShell(restrictedPath)}"
            else
                "cp ${escapeShell(sourceDir.absolutePath)} ${escapeShell(restrictedPath)}"

            Log.d(TAG, "copyToRestricted: $cmd")
            val result = runShell(cmd)
            result.success
        }

    fun isRestrictedPath(path: String): Boolean {
        return ArchiveService.isRestrictedPath(path)
    }

    fun escapeShell(path: String): String {
        return "'${path.replace("'", "'\\''")}'"
    }

    data class ShellResult(val success: Boolean, val output: String)

    fun runShell(command: String): ShellResult {
        return try {
            val process = if (isAvailable()) {
                execViaShizuku(arrayOf("sh", "-c", command))
            } else {
                Runtime.getRuntime().exec(arrayOf("sh", "-c", command))
            }
            readProcessResult(process, command)
        } catch (e: Exception) {
            Log.e(TAG, "Shell error: $command", e)
            ShellResult(false, "")
        }
    }

    private fun execViaShizuku(cmd: Array<String>): Process {
        // Shizuku.newProcess is private in v13+; use reflection to bypass
        val method = Shizuku::class.java.getDeclaredMethod(
            "newProcess",
            Array<String>::class.java,
            Array<String>::class.java,
            String::class.java
        )
        method.isAccessible = true
        return method.invoke(null, cmd, null, null) as Process
    }

    private fun readProcessResult(process: Process, command: String): ShellResult {
        val stdout = StringBuilder()
        val stderr = StringBuilder()

        val outReader = BufferedReader(InputStreamReader(process.inputStream))
        val errReader = BufferedReader(InputStreamReader(process.errorStream))

        var line: String?
        while (outReader.readLine().also { line = it } != null) {
            stdout.appendLine(line)
        }
        while (errReader.readLine().also { line = it } != null) {
            stderr.appendLine(line)
        }

        val exitCode = process.waitFor()
        val success = exitCode == 0
        if (!success) {
            Log.w(TAG, "Shell failed (exit=$exitCode): $command\nstderr: $stderr")
        }
        return ShellResult(success, stdout.toString())
    }
}
