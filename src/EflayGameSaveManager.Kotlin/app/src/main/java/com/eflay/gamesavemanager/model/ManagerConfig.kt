package com.eflay.gamesavemanager.model

import kotlinx.serialization.Serializable
import kotlinx.serialization.json.JsonElement

@Serializable
data class ManagerConfig(
    val version: String = "",
    val backup_path: String = "./save_data",
    val games: List<GameDefinition> = emptyList(),
    val settings: AppSettings = AppSettings(),
    val favorites: List<FavoriteNode> = emptyList(),
    val quick_action: QuickActionSettings = QuickActionSettings(),
    val devices: Map<String, DeviceDefinition> = emptyMap()
)

@Serializable
data class GameDefinition(
    val name: String = "",
    val save_paths: List<SaveUnitDefinition> = emptyList(),
    val game_paths: Map<String, String> = emptyMap(),
    val next_save_unit_id: Int = 0,
    val cloud_sync_enabled: Boolean = true
)

@Serializable
data class SaveUnitDefinition(
    val id: Int = 0,
    val unit_type: SaveUnitType = SaveUnitType.Folder,
    val paths: Map<String, String> = emptyMap(),
    val delete_before_apply: Boolean = false,
    val linked_unit_ids: List<Int> = emptyList()
)

@Serializable
enum class SaveUnitType {
    Folder,
    File,
    WinRegistry,
    Zip
}

@Serializable
data class AppSettings(
    val locale: String = "",
    val log_to_file: Boolean = false,
    val cloud_settings: CloudSettings = CloudSettings()
)

@Serializable
data class CloudSettings(
    val always_sync: Boolean = false,
    val auto_sync_interval: Int = 0,
    val root_path: String = "",
    val backend: CloudBackendSettings = CloudBackendSettings(),
    val max_concurrency: Int = 1
)

@Serializable
data class CloudBackendSettings(
    val type: String = "",
    val endpoint: String = "",
    val bucket: String = "",
    val region: String = "",
    val access_key_id: String = "",
    val secret_access_key: String = ""
)

@Serializable
data class FavoriteNode(
    val node_id: String = "",
    val label: String = "",
    val is_leaf: Boolean = true,
    val children: List<FavoriteNode>? = null
)

@Serializable
data class QuickActionSettings(
    val quick_action_game: JsonElement? = null,
    val hotkeys: HotkeySettings = HotkeySettings()
)

@Serializable
data class HotkeySettings(
    val apply: List<String> = emptyList(),
    val backup: List<String> = emptyList()
)

@Serializable
data class DeviceDefinition(
    val id: String = "",
    val name: String = ""
)
