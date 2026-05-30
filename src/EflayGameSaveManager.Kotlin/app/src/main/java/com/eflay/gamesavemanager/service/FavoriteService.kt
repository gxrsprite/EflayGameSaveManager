package com.eflay.gamesavemanager.service

import android.content.Context
import kotlinx.serialization.json.Json
import kotlinx.serialization.builtins.ListSerializer
import kotlinx.serialization.builtins.serializer

class FavoriteService(context: Context) {

    private val prefs = context.getSharedPreferences("favorites", Context.MODE_PRIVATE)
    private val json = Json { ignoreUnknownKeys = true }

    companion object {
        private const val KEY_FAVORITE_NAMES = "favorite_game_names"
    }

    fun load(): Set<String> {
        val jsonStr = prefs.getString(KEY_FAVORITE_NAMES, null) ?: return emptySet()
        if (jsonStr.isBlank()) return emptySet()
        return try {
            val list = json.decodeFromString(ListSerializer(String.serializer()), jsonStr)
            list.filter { it.isNotBlank() }.toSet()
        } catch (_: Exception) {
            emptySet()
        }
    }

    fun save(names: Set<String>) {
        val sorted = names.filter { it.isNotBlank() }
            .sortedBy { it.lowercase() }
        prefs.edit()
            .putString(KEY_FAVORITE_NAMES, json.encodeToString(ListSerializer(String.serializer()), sorted))
            .apply()
    }

    fun toggle(name: String): Set<String> {
        val current = load().toMutableSet()
        if (!current.add(name)) {
            current.remove(name)
        }
        save(current)
        return current
    }
}
