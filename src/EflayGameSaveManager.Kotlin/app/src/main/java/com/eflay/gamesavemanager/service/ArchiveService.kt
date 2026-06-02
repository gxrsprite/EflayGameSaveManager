package com.eflay.gamesavemanager.service

import android.util.Log
import com.eflay.gamesavemanager.model.CurrentDeviceContext
import com.eflay.gamesavemanager.model.GameSnapshot
import com.eflay.gamesavemanager.model.SaveUnitType
import java.io.File
import java.io.FileInputStream
import java.io.FileOutputStream
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
                    continue
                }
            } else {
                path
            }

            val sourceFile = File(sourcePath)
            if (!sourceFile.exists()) {
                Log.w(TAG, "Source not found after staging: $sourcePath")
                continue
            }

            when (unit.unitType) {
                SaveUnitType.Folder -> copyDirectory(sourceFile, targetDir)
                SaveUnitType.File -> {
                    sourceFile.copyTo(File(targetDir, sourceFile.name), overwrite = true)
                }
                SaveUnitType.WinRegistry, SaveUnitType.Zip -> {}
            }
        }

        val archiveFile = File(workRoot, "current-device-save.zip")
        if (archiveFile.exists()) archiveFile.delete()

        ZipOutputStream(FileOutputStream(archiveFile)).use { zip ->
            addDirectoryToZip(zip, stagingDir, "")
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
            for (unit in game.saveUnits) {
                val path = unit.path ?: continue
                if (path.isBlank()) continue

                val sourceRoot = resolveSourceRoot(extractRoot, unit.id, game.saveUnits.size)
                    ?: continue

                if (isRestrictedPath(path) && ShizukuHelper.isAvailable()) {
                    // Restore to restricted path via Shizuku
                    if (unit.deleteBeforeApply) {
                        ShizukuHelper.runShell("rm -rf ${ShizukuHelper.escapeShell(path)}")
                    }

                    val actualSource = if (unit.unitType == SaveUnitType.Folder)
                        resolveFolderContentRoot(sourceRoot, path) else sourceRoot
                    val stagingForRestore = File(extractRoot, "shizuku-out-${unit.id}")
                    stagingForRestore.mkdirs()

                    when (unit.unitType) {
                        SaveUnitType.File -> {
                            val sourceFile = actualSource.listFiles()?.firstOrNull() ?: continue
                            sourceFile.copyTo(File(stagingForRestore, sourceFile.name), overwrite = true)
                        }
                        SaveUnitType.Folder -> {
                            copyDirectory(actualSource, stagingForRestore)
                        }
                        SaveUnitType.WinRegistry, SaveUnitType.Zip -> continue
                    }

                    val success = ShizukuHelper.copyToRestricted(stagingForRestore, path)
                    if (!success) {
                        Log.w(TAG, "Shizuku restore failed for unit ${unit.id} -> $path")
                    }
                    stagingForRestore.deleteRecursively()
                } else {
                    // Normal restore via direct file I/O
                    if (unit.deleteBeforeApply) {
                        val targetFile = File(path)
                        when (unit.unitType) {
                            SaveUnitType.File -> if (targetFile.exists()) targetFile.delete()
                            SaveUnitType.Folder -> if (targetFile.exists()) targetFile.deleteRecursively()
                            SaveUnitType.WinRegistry, SaveUnitType.Zip -> {}
                        }
                    }

                    when (unit.unitType) {
                        SaveUnitType.File -> {
                            val sourceFile = sourceRoot.listFiles()?.firstOrNull() ?: continue
                            val targetFile = File(path)
                            targetFile.parentFile?.mkdirs()
                            sourceFile.copyTo(targetFile, overwrite = true)
                        }
                        SaveUnitType.Folder -> {
                            val actualSource = resolveFolderContentRoot(sourceRoot, path)
                            copyDirectory(actualSource, File(path))
                        }
                        SaveUnitType.WinRegistry, SaveUnitType.Zip -> {}
                    }
                }
            }
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
