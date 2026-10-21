package com.eflay.gamesavemanager.service

import android.util.Log
import com.eflay.gamesavemanager.model.CurrentDeviceContext
import com.eflay.gamesavemanager.model.GameSnapshot
import com.eflay.gamesavemanager.model.SaveUnitType
import java.io.File
import java.io.FileInputStream
import java.io.FileOutputStream
import java.util.zip.Deflater
import java.util.zip.ZipEntry
import java.util.zip.ZipInputStream
import java.util.zip.ZipOutputStream

class ArchiveService {

    companion object {
        private const val TAG = "ArchiveService"

        /** Paths that need Shizuku for file access (Android 11+ restrictions) */
        fun isRestrictedPath(path: String): Boolean {
            return path.startsWith("/data/data/") ||
                path.startsWith("/data/user/") ||
                path.contains("/Android/data/") ||
                path.contains("/Android/obb/")
        }
    }

    /**
     * Creates a zip archive of all current-device save paths for the given game.
     * Uses Shizuku to stage restricted paths into the archive transparently.
     */
    suspend fun createCurrentDeviceArchive(
        game: GameSnapshot,
        currentDevice: CurrentDeviceContext,
        workRoot: File
    ): File {
        workRoot.mkdirs()

        // Pure Zip mode: if all valid units are Zip type, the "archive" is just the zip file itself.
        val validUnits = game.saveUnits.filter { unit ->
            val path = unit.path
            path != null && (unit.unitType == SaveUnitType.Zip || pathExists(path))
        }
        val allZip = validUnits.isNotEmpty() && validUnits.all { it.unitType == SaveUnitType.Zip }

        if (allZip) {
            // Return the zip file directly — no staging, no repack
            val zipPath = validUnits.first().path!!
            val zipFile = File(zipPath)
            if (!zipFile.exists()) throw IllegalStateException("Zip file not found: $zipPath")
            return zipFile
        }

        val stagingDir = File(workRoot, "content")
        stagingDir.mkdirs()

        val shizukuTemp = File(workRoot, "shizuku-stage")
        var hasShizukuContent = false

        val nonZipUnits = validUnits.filter { it.unitType != SaveUnitType.Zip }
        val singleUnit = nonZipUnits.size == 1
        val skipped = mutableListOf<String>()

        for (unit in nonZipUnits) {
            val path = unit.path!!
            val targetDir = if (singleUnit) stagingDir
                else File(stagingDir, unit.id.toString()).also { it.mkdirs() }

            val sourcePath = if (isRestrictedPath(path) && ShizukuHelper.isAvailable()) {
                shizukuTemp.mkdirs()
                val tempCopy = ShizukuHelper.copyFromRestricted(path, shizukuTemp)
                if (tempCopy != null) {
                    hasShizukuContent = true
                    tempCopy.absolutePath
                } else {
                    Log.w(TAG, "Shizuku copy failed for $path, skipping unit ${unit.id}")
                    skipped.add("unit ${unit.id}: Shizuku copy failed for $path (needs root for /data/data, or Shizuku not authorized)")
                    continue
                }
            } else {
                path
            }

            val sourceFile = File(sourcePath)
            if (!sourceFile.exists()) {
                Log.w(TAG, "Source not found after staging: $sourcePath")
                skipped.add("unit ${unit.id}: not found: $sourcePath")
                continue
            }

            when (unit.unitType) {
                SaveUnitType.Folder -> {
                    if (unit.contentsOnly) {
                        // Contents-only mode uses one stable wrapper regardless of the source name.
                        SavePathFilter.copyDirectory(sourceFile, File(targetDir, "content"), unit.exclude)
                    } else {
                        SavePathFilter.copyDirectory(
                            sourceFile,
                            File(targetDir, sourceFile.name),
                            unit.exclude
                        )
                    }
                }
                SaveUnitType.File -> {
                    if (SavePathFilter.isExcluded(sourceFile.name, sourceFile.name, unit.exclude)) {
                        Log.i(TAG, "Backup skip excluded file: ${sourceFile.absolutePath}")
                    } else {
                        sourceFile.copyTo(File(targetDir, sourceFile.name), overwrite = true)
                    }
                }
                SaveUnitType.WinRegistry, SaveUnitType.Zip -> {}
            }
        }

        val archiveFile = File(workRoot, "current-device-save.zip")
        if (archiveFile.exists()) archiveFile.delete()

        // Per-game format plugin: store canonical (portable) saves in the archive.
        if (CotLSavePlugin.matches(game)) {
            CotLSavePlugin.normalizeStaging(stagingDir)
        }

        ZipOutputStream(FileOutputStream(archiveFile)).use { zip ->
            addDirectoryToZip(zip, stagingDir, "")
        }

        val entryCount = java.util.zip.ZipFile(archiveFile).use { z -> z.entries().toList().size }
        if (entryCount == 0) {
            val checked = game.saveUnits.mapNotNull { u -> u.path?.let { "[${u.unitType}] $it" } }
            throw IllegalStateException(
                "No save data was archived for '${game.name}' on this device. " +
                    "Checked: ${checked.joinToString("; ").ifEmpty { "no save paths configured for this device" }}. " +
                    "Skipped: ${skipped.joinToString("; ").ifEmpty { "none" }}. " +
                    "Nothing was uploaded — verify the save folder exists for THIS device entry and contains files."
            )
        }

        if (hasShizukuContent) {
            shizukuTemp.deleteRecursively()
        }

        return archiveFile
    }

    /**
     * Restores a cloud backup archive to the save paths.
     * Uses Shizuku to write into restricted target paths transparently.
     */
    suspend fun restoreCurrentDeviceArchive(
        archivePath: File,
        game: GameSnapshot,
        currentDevice: CurrentDeviceContext
    ) {
        // Zip type: save the downloaded zip directly without extraction
        val zipUnits = game.saveUnits.filter { it.unitType == SaveUnitType.Zip && it.path != null }
        if (zipUnits.isNotEmpty()) {
            for (unit in zipUnits) {
                val targetFile = File(unit.path!!)
                targetFile.parentFile?.mkdirs()
                archivePath.copyTo(targetFile, overwrite = true)
            }
            return
        }

        val extractRoot = File(archivePath.parentFile, "extract-${java.util.UUID.randomUUID()}")
        extractRoot.mkdirs()

        // Collect restricted units that need Shizuku
        val restrictedUnits = game.saveUnits.filter { unit ->
            val path = unit.path
            path != null && isRestrictedPath(path) && ShizukuHelper.isAvailable()
        }

        try {
            // Extract zip
            ZipInputStream(FileInputStream(archivePath)).use { zip ->
                var entry = zip.nextEntry
                while (entry != null) {
                    val targetFile = File(extractRoot, entry.name)
                    if (entry.isDirectory) {
                        targetFile.mkdirs()
                    } else {
                        targetFile.parentFile?.mkdirs()
                        FileOutputStream(targetFile).use { output ->
                            zip.copyTo(output)
                        }
                    }
                    entry = zip.nextEntry
                }
            }

            // Restore each save unit
            // Per-game format plugin: convert canonical saves into this platform's encoding.
            if (CotLSavePlugin.matches(game)) {
                CotLSavePlugin.encodeStaging(extractRoot, game, currentDevice)
            }
            for (unit in game.saveUnits) {
                val path = unit.path ?: continue
                if (path.isBlank()) continue

                val sourceRoot = resolveSourceRoot(extractRoot, unit.id, game.saveUnits.size)
                    ?: continue

                if (isRestrictedPath(path)) {
                    // Restricted paths (/Android/data/... of another app) need Shizuku shell access.
                    if (!ShizukuHelper.isAvailable()) {
                        throw IllegalStateException(
                            "恢复失败：目标目录 $path 是受限目录，必须通过 Shizuku 写入。当前 Shizuku 状态：${shizukuStatus()}。" +
                                "请打开 Shizuku 应用，确认已启动并为“Eflay 存档管理器”授权后重试。"
                        )
                    }

                    // Restore to restricted path via Shizuku
                    if (unit.deleteBeforeApply && (unit.exclude.isEmpty())) {
                        ShizukuHelper.runShell("rm -rf ${ShizukuHelper.escapeShell(path)}")
                    }
                    // Remove a stray "content" wrapper left by an older buggy restore;
                    // it is archive-internal only and must never exist on the device.
                    if (unit.contentsOnly) {
                        ShizukuHelper.runShell("rm -rf ${ShizukuHelper.escapeShell("$path/content")}")
                    }
                    // "content" is only an archive wrapper: copy its children straight
                    // into the target, never create target/content on the device.
                    val contentFolder = File(sourceRoot, "content")
                    val actualSource = if (unit.unitType == SaveUnitType.Folder && contentFolder.isDirectory)
                        contentFolder else sourceRoot
                    val stagingForRestore = File(extractRoot, "shizuku-out-${unit.id}")
                    stagingForRestore.mkdirs()

                    when (unit.unitType) {
                        SaveUnitType.File -> {
                            val sourceFile = actualSource.listFiles()?.firstOrNull() ?: continue
                            sourceFile.copyTo(File(stagingForRestore, sourceFile.name), overwrite = true)
                        }
                        SaveUnitType.Folder -> {
                            SavePathFilter.copyDirectory(actualSource, stagingForRestore, unit.exclude)
                        }
                        SaveUnitType.WinRegistry, SaveUnitType.Zip -> continue
                    }

                    val copiedEntries = stagingForRestore.listFiles()?.size ?: 0
                    if (copiedEntries == 0) {
                        stagingForRestore.deleteRecursively()
                        throw IllegalStateException(
                            "恢复失败：解出的存档为空（可能被排除规则全部过滤），未写入 $path。请检查排除规则。"
                        )
                    }

                    val success = ShizukuHelper.copyToRestricted(stagingForRestore, path)
                    stagingForRestore.deleteRecursively()
                    if (!success) {
                        throw IllegalStateException(
                            "恢复失败：Shizuku 写入 $path 失败。当前 Shizuku 状态：${shizukuStatus()}。" +
                                "常见原因：Shizuku 未运行、未授权、或 ADB 模式对目标包目录无权限（需 root/SUI）。"
                        )
                    }
                } else {
                    // Normal restore via direct file I/O
                    if (unit.deleteBeforeApply) {
                        val targetFile = File(path)
                        when (unit.unitType) {
                            SaveUnitType.File ->
                                if (targetFile.exists() &&
                                    !SavePathFilter.isExcluded(targetFile.name, targetFile.name, unit.exclude)
                                ) {
                                    targetFile.delete()
                                }
                            // Never remove locally-owned files such as settings.json.
                            SaveUnitType.Folder -> SavePathFilter.deleteExcluding(targetFile, unit.exclude)
                            SaveUnitType.WinRegistry, SaveUnitType.Zip -> {}
                        }
                    }

                    when (unit.unitType) {
                        SaveUnitType.File -> {
                            val targetFile = File(path)
                            if (SavePathFilter.isExcluded(targetFile.name, targetFile.name, unit.exclude)) {
                                Log.i(TAG, "Restore skip excluded file: $path")
                                continue
                            }
                            val sourceFile = sourceRoot.listFiles()?.firstOrNull() ?: continue
                            targetFile.parentFile?.mkdirs()
                            sourceFile.copyTo(targetFile, overwrite = true)
                        }
                        SaveUnitType.Folder -> {
                            // Unwrap the archive-internal content wrapper so files land
                            // directly under the configured target folder.
                            val contentFolder = File(sourceRoot, "content")
                            val actualSource = if (contentFolder.isDirectory) contentFolder else sourceRoot
                            SavePathFilter.copyDirectory(actualSource, File(path), unit.exclude)
                        }
                        SaveUnitType.WinRegistry, SaveUnitType.Zip -> {}
                    }
                }
            }
        } finally {
            extractRoot.deleteRecursively()
        }
    }

    /** Human-readable Shizuku state, used in error messages instead of silent failures. */
    private fun shizukuStatus(): String = when {
        !ShizukuHelper.isInstalled() -> "未安装"
        !ShizukuHelper.hasPermission() -> "已安装但未授权"
        ShizukuHelper.isRoot() -> "已授权（root 模式）"
        else -> "已授权（ADB 模式）"
    }

    fun normalizeZipArchiveForMobileRestore(archivePath: File, workRoot: File): File {
        val extractRoot = File(workRoot, "zip-normalize-${java.util.UUID.randomUUID()}")
        extractRoot.mkdirs()

        try {
            extractZipSafely(archivePath, extractRoot)

            val rootChildren = extractRoot.listFiles() ?: return archivePath
            val rootFiles = rootChildren.filter { it.isFile }
            val rootDirs = rootChildren.filter { it.isDirectory }
            if (rootFiles.isNotEmpty() || rootDirs.size != 1 || rootDirs[0].name.toIntOrNull() == null) {
                return archivePath
            }

            val normalizedArchive = File(workRoot, "mobile-restore-${java.util.UUID.randomUUID()}.zip")
            ZipOutputStream(FileOutputStream(normalizedArchive)).use { zip ->
                // Keep this fixed to ordinary ZIP deflate for mobile zip restore exports,
                // regardless of future archive compression presets elsewhere.
                zip.setLevel(Deflater.DEFAULT_COMPRESSION)
                addDirectoryToZip(zip, rootDirs[0], "")
            }
            return normalizedArchive
        } finally {
            extractRoot.deleteRecursively()
        }
    }

    private fun pathExists(path: String): Boolean {
        if (isRestrictedPath(path) && ShizukuHelper.isAvailable()) {
            // Check via Shizuku — non-blocking check, if it fails we'll skip in staging
            return true // assume exists, Shizuku copy will handle actual errors
        }
        return File(path).exists()
    }

    private fun extractZipSafely(archivePath: File, extractRoot: File) {
        val canonicalRoot = extractRoot.canonicalFile
        ZipInputStream(FileInputStream(archivePath)).use { zip ->
            var entry = zip.nextEntry
            while (entry != null) {
                val targetFile = File(extractRoot, entry.name).canonicalFile
                if (!targetFile.path.startsWith(canonicalRoot.path + File.separator)) {
                    throw IllegalStateException("Unsafe zip entry path: ${entry.name}")
                }

                if (entry.isDirectory) {
                    targetFile.mkdirs()
                } else {
                    targetFile.parentFile?.mkdirs()
                    FileOutputStream(targetFile).use { output ->
                        zip.copyTo(output)
                    }
                }
                entry = zip.nextEntry
            }
        }
    }

    private fun resolveSourceRoot(extractRoot: File, unitId: Int, totalUnitCount: Int): File? {
        val unitRoot = File(extractRoot, unitId.toString())
        if (unitRoot.exists() && unitRoot.isDirectory) return unitRoot

        if (totalUnitCount == 1) return extractRoot

        val children = extractRoot.listFiles() ?: return null
        val dirs = children.filter { it.isDirectory }
        if (dirs.size == 1) return dirs[0]

        val files = children.filter { it.isFile }
        if (files.isNotEmpty()) return extractRoot

        return null
    }

    private fun resolveFolderContentRoot(sourceRoot: File, targetPath: String): File {
        val children = sourceRoot.listFiles() ?: return sourceRoot
        val dirs = children.filter { it.isDirectory }
        val files = children.filter { it.isFile }

        if (files.isEmpty() && dirs.size == 1) {
            val targetDirName = File(targetPath).name
            if (dirs[0].name.equals(targetDirName, ignoreCase = true)) {
                return dirs[0]
            }
        }
        return sourceRoot
    }

    private fun copyDirectory(source: File, destination: File) {
        destination.mkdirs()
        source.listFiles()?.forEach { file ->
            if (file.isDirectory) {
                copyDirectory(file, File(destination, file.name))
            } else {
                file.copyTo(File(destination, file.name), overwrite = true)
            }
        }
    }

    private fun addDirectoryToZip(zip: ZipOutputStream, dir: File, basePath: String) {
        dir.listFiles()?.forEach { file ->
            val entryPath = if (basePath.isEmpty()) file.name else "$basePath/${file.name}"
            if (file.isDirectory) {
                addDirectoryToZip(zip, file, entryPath)
            } else {
                zip.putNextEntry(ZipEntry(entryPath))
                FileInputStream(file).use { input -> input.copyTo(zip) }
                zip.closeEntry()
            }
        }
    }
}
