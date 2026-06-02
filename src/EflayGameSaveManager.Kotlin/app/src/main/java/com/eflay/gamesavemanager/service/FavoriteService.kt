package com.eflay.gamesavemanager.service

import com.eflay.gamesavemanager.model.FavoriteNode
import java.util.UUID

/**
 * Manages favorites via the ManagerConfig.favorites array.
 * The ViewModel is responsible for saving the config after mutations.
 */
object FavoriteService {

    /** Parse favorite labels from config's favorites array. */
    fun loadLabels(favorites: List<FavoriteNode>): Set<String> {
        return favorites
            .filter { it.is_leaf && it.label.isNotBlank() }
            .map { it.label }
            .toSet()
    }

    /** Toggle a game in the favorites array. Returns the updated list. */
    fun toggle(favorites: List<FavoriteNode>, gameName: String): List<FavoriteNode> {
        val existing = favorites.firstOrNull { it.label == gameName }
        return if (existing != null) {
            favorites.filter { it.label != gameName }
        } else {
            favorites + FavoriteNode(
                node_id = UUID.randomUUID().toString(),
                label = gameName,
                is_leaf = true
            )
        }
    }
}
