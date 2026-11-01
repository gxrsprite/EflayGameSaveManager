package com.eflay.gamesavemanager.service

import kotlinx.serialization.json.*

internal fun updateBackupDescriptionManifest(raw: String, date: String, deviceId: String, description: String): String {
    val json = Json
    val root = json.parseToJsonElement(raw).jsonObject
    val entries = root["backups"]?.jsonArray ?: throw IllegalStateException("云端存档列表格式无效。")
    val matches = entries.filter { entry ->
        val obj = entry.jsonObject
        obj["date"]?.jsonPrimitive?.contentOrNull == date &&
            (obj["device_id"]?.jsonPrimitive?.contentOrNull ?: "").equals(deviceId, ignoreCase = true)
    }
    require(matches.size == 1) { "所选存档不存在或身份不唯一，请刷新列表。" }
    val updated = JsonArray(entries.map { entry ->
        if (entry === matches[0]) JsonObject(entry.jsonObject + ("describe" to JsonPrimitive(description.trim()))) else entry
    })
    return json.encodeToString(JsonElement.serializer(), JsonObject(root + ("backups" to updated)))
}
