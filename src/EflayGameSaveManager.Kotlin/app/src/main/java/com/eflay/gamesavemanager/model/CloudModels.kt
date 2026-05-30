package com.eflay.gamesavemanager.model

import kotlinx.serialization.SerialName
import kotlinx.serialization.Serializable

@Serializable
data class LegacyGameBackups(
    @SerialName("name") val Name: String = "",
    @SerialName("backups") val Backups: List<LegacyBackupEntry> = emptyList(),
    @SerialName("device_heads") val DeviceHeads: Map<String, String> = emptyMap(),
    @SerialName("sync_version") val SyncVersion: Int = 0
)

@Serializable
data class LegacyBackupEntry(
    @SerialName("date") val Date: String = "",
    @SerialName("describe") val Describe: String = "",
    @SerialName("path") val Path: String = "",
    @SerialName("size") val Size: Long = 0,
    @SerialName("parent") val Parent: String? = null,
    @SerialName("device_id") val DeviceId: String = ""
)

@Serializable
data class LegacySyncState(
    @SerialName("schema_version") val SchemaVersion: Int = 1,
    @SerialName("backend_fingerprint") val BackendFingerprint: String = "",
    @SerialName("current_device_id") val CurrentDeviceId: String = "",
    @SerialName("config_state") val ConfigState: LegacySyncStateItem = LegacySyncStateItem(),
    @SerialName("games") val Games: Map<String, LegacySyncStateItem> = emptyMap()
)

@Serializable
data class LegacySyncStateItem(
    @SerialName("last_known_local_head") val LastKnownLocalHead: String? = null,
    @SerialName("last_known_remote_head") val LastKnownRemoteHead: String? = null,
    @SerialName("last_sync_result") val LastSyncResult: String = "none",
    @SerialName("last_sync_at") val LastSyncAt: String? = null,
    @SerialName("pending_action") val PendingAction: String = "none"
)

data class GameCloudStatus(
    val gameName: String,
    val isAvailable: Boolean,
    val rootKey: String,
    val currentHead: String?,
    val backupCount: Int,
    val currentHeadDate: String?
)

data class CloudGameBackup(
    val gameName: String,
    val date: String,
    val describe: String,
    val path: String,
    val size: Long,
    val parent: String?,
    val deviceId: String,
    val isCurrentDeviceHead: Boolean,
    val isAnyDeviceHead: Boolean
)

data class CloudUploadResult(
    val rootKey: String,
    val uploadedObjectCount: Int,
    val uploadedByteCount: Long,
    val timestamp: Long = System.currentTimeMillis()
)

data class CloudDownloadResult(
    val rootKey: String,
    val downloadedObjectCount: Int,
    val downloadedByteCount: Long,
    val archivePath: String? = null,
    val timestamp: Long = System.currentTimeMillis()
)
