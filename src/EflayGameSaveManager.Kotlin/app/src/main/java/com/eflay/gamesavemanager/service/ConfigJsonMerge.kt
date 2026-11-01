package com.eflay.gamesavemanager.service

import kotlinx.serialization.json.*

internal fun mergeConfig(old: JsonElement?, updated: JsonElement, field: String = ""): JsonElement {
    if (old is JsonObject && updated is JsonObject) {
        if (field == "paths" || field == "game_paths") return updated
        val result = if (field == "devices") mutableMapOf() else old.toMutableMap()
        updated.forEach { (key, value) -> result[key] = mergeConfig(old[key], value, key) }
        return JsonObject(result)
    }
    if (old is JsonArray && updated is JsonArray) {
        val key = when (field) { "games" -> "name"; "save_paths" -> "id"; "favorites", "children" -> "node_id"; else -> return updated }
        return JsonArray(updated.map { item ->
            val id = (item as? JsonObject)?.get(key)
            val matches = old.filter { (it as? JsonObject)?.get(key) == id }
            mergeConfig(if (id != null && matches.size == 1) matches[0] else null, item)
        })
    }
    return updated
}
