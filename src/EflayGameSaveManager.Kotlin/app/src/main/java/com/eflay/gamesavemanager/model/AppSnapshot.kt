package com.eflay.gamesavemanager.model

data class AppSnapshot(
    val games: List<GameSnapshot>,
    val currentDevice: CurrentDeviceContext
)

data class GameSnapshot(
    val name: String,
    val cloudSyncEnabled: Boolean,
    val saveUnits: List<SaveUnitSnapshot>
)

data class SaveUnitSnapshot(
    val id: Int,
    val unitType: SaveUnitType,
    val path: String?,
    val deleteBeforeApply: Boolean = false
)

data class CurrentDeviceContext(
    val deviceId: String,
    val deviceName: String
)
