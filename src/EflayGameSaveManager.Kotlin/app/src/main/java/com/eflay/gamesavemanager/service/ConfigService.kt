package com.eflay.gamesavemanager.service

import android.content.Context
import com.eflay.gamesavemanager.model.*
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import kotlinx.serialization.json.Json
import java.io.File

class ConfigService(private val context: Context) {

    private val json = Json {
        ignoreUnknownKeys = true
        prettyPrint = true
        encodeDefaults = true
    }

    companion object {
        const val CONFIG_FILE_NAME = "GameSaveManager.config.json"
        const val RUNTIME_FILE_NAME = "GameSaveManager.runtime.json"
    }

    suspend fun loadConfig(configPath: String): ManagerConfig = withContext(Dispatchers.IO) {
        val file = File(configPath)
        if (!file.exists()) {
            return@withContext createDefault()
        }
        val content = file.readText()
        json.decodeFromString(ManagerConfig.serializer(), content)
    }

    suspend fun saveConfig(configPath: String, config: ManagerConfig) = withContext(Dispatchers.IO) {
        val file = File(configPath)
        file.parentFile?.mkdirs()
        file.writeText(json.encodeToString(ManagerConfig.serializer(), config))
    }

    fun createDefault(): ManagerConfig {
        return ManagerConfig(
            version = "1.8.0",
            backup_path = "./save_data",
            settings = AppSettings(
                locale = "zh_SIMPLIFIED",
                log_to_file = true,
                cloud_settings = CloudSettings(
                    root_path = "/game-save-manager",
                    backend = CloudBackendSettings(
                        type = "S3",
                        endpoint = "http://124.220.236.154:9000",
                        bucket = "eflay-game-save",
                        region = "eflay",
                        access_key_id = "minio",
                        secret_access_key = "12345678"
                    )
                )
            )
        )
    }

    fun getWorkspaceDir(): File {
        return File(context.filesDir, "workspace")
    }

    fun getConfigPath(): String {
        val dir = getWorkspaceDir()
        return File(dir, CONFIG_FILE_NAME).absolutePath
    }

    fun getRuntimePath(): String {
        val dir = getWorkspaceDir()
        return File(dir, RUNTIME_FILE_NAME).absolutePath
    }

    fun createSnapshot(config: ManagerConfig, configPath: String, deviceName: String): AppSnapshot {
        val deviceId = resolveCurrentDeviceId(config, deviceName)
        val games = config.games.map { game ->
            val saveUnits = game.save_paths.map { unit ->
                val path = unit.paths[deviceId]
                SaveUnitSnapshot(
                    id = unit.id,
                    unitType = unit.unit_type,
                    path = path,
                    deleteBeforeApply = unit.delete_before_apply
                )
            }
            GameSnapshot(
                name = game.name,
                cloudSyncEnabled = game.cloud_sync_enabled,
                saveUnits = saveUnits
            )
        }
        return AppSnapshot(
            games = games,
            currentDevice = CurrentDeviceContext(deviceId = deviceId, deviceName = deviceName)
        )
    }

    /**
     * Normalizes the config by merging duplicate same-type save units within each game.
     * When PC and Android paths were stored as separate units of the same type,
     * this merges them back into one unit with per-device paths (MAUI pitfalls #11, #12).
     * Returns the normalized config (may be the same instance if no changes needed).
     */
    fun normalizeConfig(config: ManagerConfig): ManagerConfig {
        var changed = false
        val normalizedGames = config.games.map { game ->
            val merged = mergeDuplicateSaveUnits(game.save_paths)
            if (merged.size != game.save_paths.size) {
                changed = true
                game.copy(
                    save_paths = merged,
                    next_save_unit_id = maxOf(game.next_save_unit_id, (merged.maxOfOrNull { it.id } ?: 0) + 1)
                )
            } else {
                game
            }
        }
        return if (changed) config.copy(games = normalizedGames) else config
    }

    /**
     * Merges save units that share the same type by combining their per-device paths
     * into the lowest-ID unit of that group. Units with conflicting device entries
     * keep the first occurrence.
     */
    private fun mergeDuplicateSaveUnits(units: List<SaveUnitDefinition>): List<SaveUnitDefinition> {
        val grouped = units.groupBy { it.unit_type }
        val merged = mutableListOf<SaveUnitDefinition>()

        for ((_, group) in grouped) {
            if (group.size == 1) {
                merged.addAll(group)
                continue
            }

            // Merge all paths into the lowest-ID unit
            val primary = group.minByOrNull { it.id }!!
            val mergedPaths = primary.paths.toMutableMap()
            for (unit in group) {
                if (unit.id == primary.id) continue
                for ((deviceId, path) in unit.paths) {
                    if (!mergedPaths.containsKey(deviceId)) {
                        mergedPaths[deviceId] = path
                    }
                }
            }
            merged.add(primary.copy(paths = mergedPaths))
        }

        return merged.sortedBy { it.id }
    }

    /**
     * Resolves device ID. First checks runtime.json for forced_device_name,
     * then matches by device name in config devices.
     */
    fun resolveCurrentDeviceId(config: ManagerConfig, deviceName: String): String {
        // Check runtime.json for forced device name
        val runtimeFile = File(getRuntimePath())
        if (runtimeFile.exists()) {
            try {
                val runtimeJson = Json { ignoreUnknownKeys = true }
                val runtimeMap: Map<String, kotlinx.serialization.json.JsonElement> =
                    runtimeJson.decodeFromString(runtimeFile.readText())
                val forcedName = runtimeMap["forced_device_name"]?.let {
                    it.toString().trim('"')
                }
                if (!forcedName.isNullOrBlank()) {
                    // Find device ID by forced name
                    config.devices.entries.firstOrNull { it.value.name == forcedName }?.key?.let {
                        return it
                    }
                }
            } catch (_: Exception) {
                // Ignore parse errors
            }
        }

        // Match by device name in config
        config.devices.entries.firstOrNull { it.value.name == deviceName }?.key?.let {
            return it
        }

        // Check if "Android" device exists in config
        config.devices.entries.firstOrNull { it.value.name == "Android" }?.key?.let {
            return it
        }

        // Fallback: return the first device ID or generate a stable one
        return config.devices.keys.firstOrNull()
            ?: "cd0d180b-fd0e-416b-bb12-11c9b18fdd50" // Default Android device ID from config
    }
}
