package com.eflay.gamesavemanager.service

import android.content.Context
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.io.File

class WorkspaceService(private val context: Context) {

    suspend fun ensureConfigPath(configService: ConfigService): String = withContext(Dispatchers.IO) {
        val workspaceDir = configService.getWorkspaceDir()
        workspaceDir.mkdirs()

        val configPath = configService.getConfigPath()
        if (!File(configPath).exists()) {
            val seeded = trySeedFromAssets(ConfigService.CONFIG_FILE_NAME, configPath)
            if (!seeded) {
                configService.saveConfig(configPath, configService.createDefault())
            }
        }

        val runtimePath = configService.getRuntimePath()
        if (!File(runtimePath).exists()) {
            trySeedFromAssets(ConfigService.RUNTIME_FILE_NAME, runtimePath)
        }

        // Pitfall #11/#12: normalize on every startup so stale mobile configs
        // from older APK installs get merged duplicate units cleaned up.
        if (File(configPath).exists()) {
            val config = configService.loadConfig(configPath)
            val normalized = configService.normalizeConfig(config)
            if (normalized != config) {
                configService.saveConfig(configPath, normalized)
            }
        }

        configPath
    }

    private fun trySeedFromAssets(assetName: String, destinationPath: String): Boolean {
        return try {
            context.assets.open(assetName).use { source ->
                val destFile = File(destinationPath)
                destFile.parentFile?.mkdirs()
                destFile.outputStream().use { dest ->
                    source.copyTo(dest)
                }
            }
            true
        } catch (_: Exception) {
            false
        }
    }
}
