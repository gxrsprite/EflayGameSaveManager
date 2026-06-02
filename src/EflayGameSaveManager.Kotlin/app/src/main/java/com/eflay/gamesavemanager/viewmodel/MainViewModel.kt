package com.eflay.gamesavemanager.viewmodel

import android.app.Application
import android.os.Build
import android.util.Log
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import com.eflay.gamesavemanager.model.*
import com.eflay.gamesavemanager.service.*
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.io.File

class MainViewModel(application: Application) : AndroidViewModel(application) {

    companion object {
        private const val TAG = "MainViewModel"
    }

    private val configService = ConfigService(application)
    private val workspaceService = WorkspaceService(application)
    private val s3CloudService = S3CloudService()
    private val archiveService = ArchiveService()
    private var currentConfig: ManagerConfig? = null
    private var currentSnapshot: AppSnapshot? = null
    private var configPath: String = ""
    private var favoriteGameNames: Set<String> = emptySet()

    // UI State
    data class UiState(
        val statusMessage: String = "Preparing mobile save sync workspace...",
        val configPathSummary: String = "Config: pending",
        val currentDeviceSummary: String = "Device: pending",
        val games: List<GameListItem> = emptyList(),
        val favoriteGames: List<GameListItem> = emptyList(),
        val selectedGame: GameListItem? = null,
        val selectedGameTitle: String = "Select a game",
        val selectedGameSummary: String = "Cloud sync and backup details will show here.",
        val selectedGameDetails: String = "No game selected.",
        val cloudDetails: String = "Cloud status not loaded.",
        val isBusy: Boolean = false,
        val isAddGameVisible: Boolean = false,
        val addGameName: String = "",
        val addSavePath: String = "",
        val addGamePath: String = "",
        val selectedSaveUnitType: SaveUnitType = SaveUnitType.Folder,
        val saveUnitTargets: List<SaveUnitTargetOption> = emptyList(),
        val selectedSaveUnitTarget: SaveUnitTargetOption? = null,
        val editSavePath: String = "",
        val editGamePath: String = "",
        val hasNoFavoriteGames: Boolean = true,
        val shizukuStatus: String = "Shizuku: checking...",
        val showZipRestorePicker: Boolean = false,
        val showZipUploadPicker: Boolean = false
    )

    private val _uiState = MutableStateFlow(UiState())
    val uiState: StateFlow<UiState> = _uiState.asStateFlow()

    init {
        initialize()
    }

    fun initialize() {
        viewModelScope.launch {
            reload()
        }
    }

    fun reload() {
        viewModelScope.launch {
            runBusy("Loading mobile configuration...") {
                reloadCore()
            }
        }
    }

    private suspend fun reloadCore() {
        configPath = workspaceService.ensureConfigPath(configService)
        currentConfig = configService.loadConfig(configPath)
        favoriteGameNames = FavoriteService.loadLabels(currentConfig!!.favorites)

        val deviceName = "${Build.MANUFACTURER} ${Build.MODEL}".trim()
        currentSnapshot = configService.createSnapshot(currentConfig!!, configPath, deviceName)

        val snapshot = currentSnapshot!!
        val games = snapshot.games.map { game ->
            GameListItem(
                name = game.name,
                cloudSyncEnabled = game.cloudSyncEnabled,
                saveUnits = game.saveUnits,
                isFavorite = favoriteGameNames.contains(game.name)
            )
        }

        val favorites = games.filter { it.isFavorite }

        val previousSelection = _uiState.value.selectedGame?.name
        val selectedGame = games.firstOrNull { it.name == previousSelection }
            ?: games.firstOrNull()

        val shizukuStatus = when {
            !ShizukuHelper.isInstalled() -> "Shizuku: not installed"
            !ShizukuHelper.hasPermission() -> "Shizuku: no permission (open Shizuku app to authorize)"
            ShizukuHelper.isRoot() -> "Shizuku: ready (root — full /data/data access)"
            else -> "Shizuku: ready (ADB — /data/data may be restricted)"
        }

        _uiState.value = _uiState.value.copy(
            configPathSummary = "Config: $configPath",
            currentDeviceSummary = "Device: ${snapshot.currentDevice.deviceName} [${snapshot.currentDevice.deviceId}] | Platform: Android",
            shizukuStatus = shizukuStatus,
            games = games,
            favoriteGames = favorites,
            hasNoFavoriteGames = favorites.isEmpty(),
            statusMessage = if (games.isEmpty()) "No games found yet. Add the Android games you want to sync."
            else "Loaded ${games.size} games."
        )

        if (selectedGame != null) {
            selectGame(selectedGame)
        }
    }

    // --- Game selection ---

    fun selectGame(game: GameListItem) {
        val snapshot = currentSnapshot ?: return
        val gameDef = currentConfig?.games?.firstOrNull { it.name.equals(game.name, ignoreCase = true) }
        val deviceId = snapshot.currentDevice.deviceId

        val details = buildGameDetails(game, gameDef, deviceId)
        val targets = buildSaveUnitTargets(gameDef)

        val currentPath = getExplicitCurrentDevicePath(gameDef, deviceId)
        val editGamePath = getExplicitCurrentDeviceGamePath(gameDef, deviceId)

        _uiState.value = _uiState.value.copy(
            selectedGame = game,
            selectedGameTitle = game.name,
            selectedGameSummary = "${if (game.cloudSyncEnabled) "Cloud" else "Local"} | " +
                "${game.saveUnits.count { it.unitType == SaveUnitType.Folder }} folder, " +
                "${game.saveUnits.count { it.unitType == SaveUnitType.File }} file",
            selectedGameDetails = details,
            cloudDetails = "Cloud status not loaded.",
            saveUnitTargets = targets,
            selectedSaveUnitTarget = currentPath?.let { cp ->
                targets.firstOrNull { !it.isNew && it.saveUnitId == cp.saveUnitId }
            } ?: targets.firstOrNull { !it.isNew } ?: targets.firstOrNull(),
            editSavePath = currentPath?.path ?: "",
            editGamePath = editGamePath
        )
    }

    // --- Favorites ---

    fun toggleFavorite(game: GameListItem) {
        val config = currentConfig ?: return
        val updated = FavoriteService.toggle(config.favorites, game.name)
        currentConfig = config.copy(favorites = updated)
        favoriteGameNames = FavoriteService.loadLabels(updated)
        viewModelScope.launch {
            configService.saveConfig(configPath, currentConfig!!)
            reloadCore()
            val updatedGame = _uiState.value.games.firstOrNull { it.name == game.name }
            if (updatedGame != null) selectGame(updatedGame)
        }
    }

    // --- Add game ---

    fun toggleAddGame() {
        _uiState.value = _uiState.value.copy(
            isAddGameVisible = !_uiState.value.isAddGameVisible
        )
    }

    fun updateAddGameName(value: String) {
        _uiState.value = _uiState.value.copy(addGameName = value)
    }

    fun updateAddSavePath(value: String) {
        _uiState.value = _uiState.value.copy(addSavePath = value)
    }

    fun updateAddGamePath(value: String) {
        _uiState.value = _uiState.value.copy(addGamePath = value)
    }

    fun updateSelectedSaveUnitType(type: SaveUnitType) {
        _uiState.value = _uiState.value.copy(selectedSaveUnitType = type)
    }

    fun addGame() {
        val config = currentConfig ?: run {
            setStatus("Configuration is not loaded yet.")
            return
        }
        val snapshot = currentSnapshot ?: run {
            setStatus("Configuration is not loaded yet.")
            return
        }

        val state = _uiState.value
        val gameName = state.addGameName.trim()
        val savePath = state.addSavePath.trim()
        val gamePath = state.addGamePath.trim()

        if (gameName.isBlank()) { setStatus("Game name is required."); return }
        if (state.selectedSaveUnitType != SaveUnitType.Zip && savePath.isBlank()) {
            setStatus("Save path is required."); return
        }
        if (savePath.isNotBlank() && !isSupportedAndroidPath(savePath)) {
            setStatus("Android save path must be an absolute path or content:// URI.")
            return
        }
        if (gamePath.isNotBlank() && !isSupportedAndroidPath(gamePath)) {
            setStatus("Android game path must be an absolute path or content:// URI.")
            return
        }
        // Zip mode: allow adding to existing game (auto-link with Folder unit from PC)
        val isZipMode = state.selectedSaveUnitType == SaveUnitType.Zip
        val existingGame = config.games.firstOrNull { it.name.equals(gameName, ignoreCase = true) }

        viewModelScope.launch {
            runBusy("Adding $gameName...") {
                val updatedConfig: ManagerConfig
                if (isZipMode && existingGame != null) {
                    // Add Zip unit to existing game, link to first Folder unit
                    val folderUnit = existingGame.save_paths.firstOrNull { it.unit_type == SaveUnitType.Folder }
                    val newZipUnit = SaveUnitDefinition(
                        id = existingGame.next_save_unit_id,
                        unit_type = SaveUnitType.Zip,
                        linked_unit_ids = if (folderUnit != null) listOf(folderUnit.id) else emptyList()
                    )
                    // Add bidirectional link
                    if (folderUnit != null) {
                        val updatedFolderUnit = folderUnit.copy(
                            linked_unit_ids = folderUnit.linked_unit_ids + newZipUnit.id
                        )
                        val updatedSavePaths = existingGame.save_paths.map {
                            if (it.id == folderUnit.id) updatedFolderUnit else it
                        } + newZipUnit
                        val updatedGame = existingGame.copy(
                            save_paths = updatedSavePaths,
                            next_save_unit_id = existingGame.next_save_unit_id + 1
                        )
                        updatedConfig = config.copy(
                            games = config.games.map { if (it.name.equals(gameName, ignoreCase = true)) updatedGame else it }
                        )
                    } else {
                        val updatedGame = existingGame.copy(
                            save_paths = existingGame.save_paths + newZipUnit,
                            next_save_unit_id = existingGame.next_save_unit_id + 1
                        )
                        updatedConfig = config.copy(
                            games = config.games.map { if (it.name.equals(gameName, ignoreCase = true)) updatedGame else it }
                        )
                    }
                } else {
                    val newGame = GameDefinition(
                        name = gameName,
                        save_paths = listOf(
                            SaveUnitDefinition(
                                id = 0,
                                unit_type = state.selectedSaveUnitType,
                                delete_before_apply = false,
                                paths = if (savePath.isNotBlank()) mapOf(snapshot.currentDevice.deviceId to savePath) else emptyMap()
                            )
                        ),
                        game_paths = if (gamePath.isNotBlank())
                            mapOf(snapshot.currentDevice.deviceId to gamePath) else emptyMap(),
                        next_save_unit_id = 1,
                        cloud_sync_enabled = true
                    )
                    updatedConfig = config.copy(games = config.games + newGame)
                }
                configService.saveConfig(configPath, updatedConfig)
                currentConfig = updatedConfig

                _uiState.value = _uiState.value.copy(
                    addGameName = "",
                    addSavePath = "",
                    addGamePath = "",
                    isAddGameVisible = false
                )

                reloadCore()
                val addedGame = _uiState.value.games.firstOrNull { it.name.equals(gameName, ignoreCase = true) }
                if (addedGame != null) selectGame(addedGame)
                setStatus("Added game: $gameName")
            }
        }
    }

    // --- Save paths editing ---

    fun updateEditSavePath(value: String) {
        _uiState.value = _uiState.value.copy(editSavePath = value)
    }

    fun updateEditGamePath(value: String) {
        _uiState.value = _uiState.value.copy(editGamePath = value)
    }

    fun updateSelectedSaveUnitTarget(target: SaveUnitTargetOption) {
        _uiState.value = _uiState.value.copy(selectedSaveUnitTarget = target)
    }

    fun saveSelectedGamePaths() {
        val config = currentConfig ?: run { setStatus("Select a game first."); return }
        val snapshot = currentSnapshot ?: run { setStatus("Select a game first."); return }
        val state = _uiState.value
        val selectedGame = state.selectedGame ?: run { setStatus("Select a game first."); return }
        val target = state.selectedSaveUnitTarget ?: run { setStatus("Pick a save unit first."); return }

        val savePath = state.editSavePath.trim()
        val gamePath = state.editGamePath.trim()

        if (savePath.isBlank()) { setStatus("Save path is required."); return }
        if (!isSupportedAndroidPath(savePath)) {
            setStatus("Android save path must be an absolute path or content:// URI.")
            return
        }
        if (gamePath.isNotBlank() && !isSupportedAndroidPath(gamePath)) {
            setStatus("Android game path must be an absolute path or content:// URI.")
            return
        }

        viewModelScope.launch {
            runBusy("Saving Android paths for ${selectedGame.name}...") {
                val game = config.games.firstOrNull { it.name.equals(selectedGame.name, ignoreCase = true) }
                    ?: throw IllegalStateException("Game not found: ${selectedGame.name}")

                val deviceId = snapshot.currentDevice.deviceId

                val targetUnit = if (target.isNew) {
                    // Pitfall #11: prefer merging into an existing same-type unit
                    // that doesn't yet have a path for this device, instead of
                    // creating a duplicate unit.
                    val existing = game.save_paths.firstOrNull { unit ->
                        unit.unit_type == target.unitType &&
                            !unit.paths.containsKey(deviceId)
                    }
                    if (existing != null) {
                        existing
                    } else {
                        val newUnit = SaveUnitDefinition(
                            id = game.next_save_unit_id,
                            unit_type = target.unitType,
                            delete_before_apply = false,
                            paths = mutableMapOf()
                        )
                        val updatedGame = game.copy(
                            save_paths = game.save_paths + newUnit,
                            next_save_unit_id = game.next_save_unit_id + 1
                        )
                        val idx = config.games.indexOfFirst { it.name.equals(game.name, ignoreCase = true) }
                        config.games.toMutableList().apply { set(idx, updatedGame) }
                        newUnit
                    }
                } else {
                    game.save_paths.firstOrNull { it.id == target.saveUnitId }
                        ?: throw IllegalStateException("Save unit not found: ${target.saveUnitId}")
                }

                val updatedPaths = targetUnit.paths.toMutableMap()
                updatedPaths[deviceId] = savePath

                val updatedUnit = targetUnit.copy(paths = updatedPaths)
                val updatedSavePaths = game.save_paths.map { unit ->
                    if (unit.id == updatedUnit.id) updatedUnit else unit
                }

                val updatedGamePaths = game.game_paths.toMutableMap()
                if (gamePath.isBlank()) updatedGamePaths.remove(deviceId)
                else updatedGamePaths[deviceId] = gamePath

                val updatedGame = game.copy(
                    save_paths = updatedSavePaths,
                    game_paths = updatedGamePaths
                )

                val updatedGames = config.games.map { g ->
                    if (g.name.equals(game.name, ignoreCase = true)) updatedGame else g
                }

                val updatedConfig = config.copy(games = updatedGames)
                configService.saveConfig(configPath, updatedConfig)
                currentConfig = updatedConfig

                reloadCore()
                val refreshedGame = _uiState.value.games.firstOrNull {
                    it.name.equals(game.name, ignoreCase = true)
                }
                if (refreshedGame != null) selectGame(refreshedGame)
                setStatus("Updated Android paths for ${game.name}.")
            }
        }
    }

    // --- Cloud operations ---

    fun refreshCloud() {
        val ctx = getCloudContext() ?: run {
            Log.w(TAG, "refreshCloud: getCloudContext returned null, status=${_uiState.value.statusMessage}")
            return
        }
        Log.d(TAG, "refreshCloud: game=${ctx.game.name}, endpoint=${ctx.cloudSettings.backend.endpoint}")
        viewModelScope.launch {
            runBusy("Refreshing cloud status for ${ctx.game.name}...") {
                refreshCloudCore(ctx.game, ctx.snapshot, ctx.cloudSettings)
            }
        }
    }

    private suspend fun refreshCloudCore(
        game: GameSnapshot,
        snapshot: AppSnapshot,
        cloudSettings: CloudSettings
    ) {
        val status = s3CloudService.getGameStatus(game, snapshot.currentDevice, cloudSettings)
        val backups = s3CloudService.listGameBackups(game, snapshot.currentDevice, cloudSettings)
        _uiState.value = _uiState.value.copy(
            cloudDetails = buildCloudDetails(status, backups),
            statusMessage = "Cloud status refreshed for ${game.name}."
        )
    }

    fun uploadCurrent() {
        val ctx = getCloudContext() ?: return
        if (!hasCurrentDevicePaths(ctx.game.name, ctx.snapshot.currentDevice.deviceId)) {
            setStatus("No save paths configured for this device. Edit the game to add Android save paths first.")
            return
        }

        // Zip mode: ask user to select a zip file to upload
        val hasZipUnits = ctx.game.saveUnits.any { it.unitType == SaveUnitType.Zip }
        if (hasZipUnits) {
            _uiState.value = _uiState.value.copy(showZipUploadPicker = true)
            setStatus("Select a zip file to upload...")
            return
        }

        viewModelScope.launch {
            runBusy("Uploading ${ctx.game.name} current save...") {
                performUpload(ctx, null)
            }
        }
    }

    /** Called from UI after user picks a zip file for Zip-mode upload */
    fun uploadCurrentWithZipPath(zipPath: String) {
        _uiState.value = _uiState.value.copy(showZipUploadPicker = false)
        if (zipPath.isBlank()) {
            setStatus("Upload cancelled — no file selected.")
            return
        }

        val ctx = getCloudContext() ?: return

        viewModelScope.launch {
            runBusy("Uploading ${ctx.game.name} current save...") {
                performUpload(ctx, zipPath)
            }
        }
    }

    private suspend fun performUpload(ctx: CloudContext, zipPath: String?) {
        val workDir = withContext(Dispatchers.IO) {
            val dir = File(getApplication<Application>().cacheDir, "upload-${java.util.UUID.randomUUID()}")
            dir.mkdirs(); dir
        }
        try {
            val archivePath = if (zipPath != null) {
                // Zip mode: use the picked zip directly
                File(zipPath).also { if (!it.exists()) throw IllegalStateException("Zip file not found: $zipPath") }
            } else {
                archiveService.createCurrentDeviceArchive(ctx.game, ctx.snapshot.currentDevice, workDir)
            }
            val result = s3CloudService.uploadCurrentSave(
                ctx.game, ctx.snapshot.currentDevice, ctx.cloudSettings, archivePath.absolutePath
            )
            setStatus("Uploaded current save to ${result.rootKey}.")
            refreshCloudCore(ctx.game, ctx.snapshot, ctx.cloudSettings)
        } finally {
            workDir.deleteRecursively()
        }
    }

    fun restoreCurrent() {
        val ctx = getCloudContext() ?: return
        if (!hasCurrentDevicePaths(ctx.game.name, ctx.snapshot.currentDevice.deviceId)) {
            setStatus("No save paths configured for this device. Edit the game to add Android save paths first.")
            return
        }

        // Zip mode: ask user where to save the downloaded zip file
        val hasZipUnits = ctx.game.saveUnits.any { it.unitType == SaveUnitType.Zip }
        if (hasZipUnits) {
            _uiState.value = _uiState.value.copy(showZipRestorePicker = true)
            setStatus("Select where to save the restored zip...")
            return
        }

        viewModelScope.launch {
            runBusy("Restoring ${ctx.game.name} latest cloud save...") {
                performRestore(ctx)
            }
        }
    }

    /** Called from UI after user picks save location for Zip restore (URI from CreateDocument) */
    fun restoreCurrentWithZipPath(zipSaveUri: String) {
        _uiState.value = _uiState.value.copy(showZipRestorePicker = false)
        if (zipSaveUri.isBlank()) {
            setStatus("Restore cancelled — no save location selected.")
            return
        }

        val ctx = getCloudContext() ?: return

        viewModelScope.launch {
            runBusy("Restoring ${ctx.game.name} latest cloud save...") {
                val workDir = withContext(Dispatchers.IO) {
                    val dir = File(getApplication<Application>().cacheDir, "restore-${java.util.UUID.randomUUID()}")
                    dir.mkdirs(); dir
                }
                try {
                    // Download the zip to temp
                    val result = s3CloudService.restoreCurrentSave(
                        ctx.game, ctx.snapshot.currentDevice, ctx.cloudSettings, workDir.absolutePath
                    )
                    result.archivePath?.let { archivePath ->
                        val archiveFile = File(archivePath)
                        if (archiveFile.exists()) {
                            // Write the downloaded zip to the user-chosen URI
                            val uri = android.net.Uri.parse(zipSaveUri)
                            if (zipSaveUri.startsWith("content://")) {
                                // Use ContentResolver for content URIs
                                getApplication<Application>().contentResolver.openOutputStream(uri)?.use { out ->
                                    archiveFile.inputStream().use { it.copyTo(out) }
                                }
                            } else {
                                // Use direct file I/O for real paths
                                val targetFile = File(zipSaveUri)
                                targetFile.parentFile?.mkdirs()
                                archiveFile.copyTo(targetFile, overwrite = true)
                            }
                        }
                    }
                    setStatus("Restored latest cloud save from ${result.rootKey}.")
                    refreshCloudCore(ctx.game, ctx.snapshot, ctx.cloudSettings)
                } finally {
                    workDir.deleteRecursively()
                }
            }
        }
    }

    private suspend fun performRestore(ctx: CloudContext) {
        val workDir = withContext(Dispatchers.IO) {
            val dir = File(getApplication<Application>().cacheDir, "restore-${java.util.UUID.randomUUID()}")
            dir.mkdirs()
            dir
        }
        try {
            val result = s3CloudService.restoreCurrentSave(
                ctx.game, ctx.snapshot.currentDevice, ctx.cloudSettings, workDir.absolutePath
            )
            result.archivePath?.let { archivePath ->
                val archiveFile = File(archivePath)
                if (archiveFile.exists()) {
                    archiveService.restoreCurrentDeviceArchive(
                        archiveFile, ctx.game, ctx.snapshot.currentDevice
                    )
                }
            }
            setStatus("Restored latest cloud save from ${result.rootKey}.")
            refreshCloudCore(ctx.game, ctx.snapshot, ctx.cloudSettings)
        } finally {
            workDir.deleteRecursively()
        }
    }

    // --- Helpers ---

    private suspend fun runBusy(message: String, block: suspend () -> Unit) {
        val state = _uiState.value
        if (state.isBusy) {
            setStatus("Another operation is still running.")
            return
        }
        _uiState.value = _uiState.value.copy(isBusy = true, statusMessage = message)
        try {
            block()
        } catch (e: Exception) {
            setStatus(e.message ?: "Unknown error")
        } finally {
            _uiState.value = _uiState.value.copy(isBusy = false)
        }
    }

    fun setStatusMessage(message: String) {
        _uiState.value = _uiState.value.copy(statusMessage = message)
    }

    // --- Config Editor ---
    fun loadConfigRaw(): String {
        return try {
            java.io.File(configPath).readText()
        } catch (_: Exception) {
            "{}"
        }
    }

    fun saveConfigRaw(rawJson: String) {
        viewModelScope.launch {
            runBusy("Saving config...") {
                try {
                    configService.saveConfigRaw(configPath, rawJson)
                    reloadCore()
                    setStatus("Config saved and reloaded.")
                } catch (e: Exception) {
                    setStatus("Invalid JSON: ${e.message}")
                }
            }
        }
    }

    fun importConfigFile(uri: android.net.Uri) {
        viewModelScope.launch {
            runBusy("Importing config...") {
                try {
                    val stream = getApplication<Application>().contentResolver.openInputStream(uri)
                        ?: throw IllegalStateException("Cannot open file")
                    val content = stream.bufferedReader().readText()
                    stream.close()
                    configService.saveConfigRaw(configPath, content)
                    reloadCore()
                    setStatus("Config imported successfully.")
                } catch (e: Exception) {
                    setStatus("Import failed: ${e.message}")
                }
            }
        }
    }

    private fun setStatus(message: String) {
        _uiState.value = _uiState.value.copy(statusMessage = message)
    }

    private fun hasCurrentDevicePaths(gameName: String, deviceId: String): Boolean {
        val gameDef = currentConfig?.games?.firstOrNull { it.name.equals(gameName, ignoreCase = true) }
        return gameDef?.save_paths?.any { unit ->
            unit.paths.containsKey(deviceId) || unit.unit_type == SaveUnitType.Zip
        } == true
    }

    private data class CloudContext(
        val game: GameSnapshot,
        val snapshot: AppSnapshot,
        val cloudSettings: CloudSettings
    )

    private fun getCloudContext(): CloudContext? {
        val state = _uiState.value
        val snapshot = currentSnapshot ?: run { setStatus("Configuration not loaded."); return null }
        val config = currentConfig ?: run { setStatus("Configuration not loaded."); return null }
        val selectedGame = state.selectedGame ?: run { setStatus("Select a game first."); return null }

        val cloudSettings = config.settings.cloud_settings
        if (cloudSettings.backend.type != "S3" ||
            cloudSettings.backend.endpoint.isBlank() ||
            cloudSettings.backend.bucket.isBlank() ||
            cloudSettings.backend.access_key_id.isBlank() ||
            cloudSettings.backend.secret_access_key.isBlank()
        ) {
            setStatus("Cloud backend configuration is incomplete.")
            return null
        }

        val game = snapshot.games.firstOrNull { it.name.equals(selectedGame.name, ignoreCase = true) }
            ?: run { setStatus("Game not found in snapshot."); return null }

        return CloudContext(game, snapshot, cloudSettings)
    }

    private fun buildGameDetails(game: GameListItem, gameDef: GameDefinition?, deviceId: String): String {
        val sb = StringBuilder()
        sb.appendLine("Game: ${game.name}")
        sb.appendLine("Cloud sync enabled: ${game.cloudSyncEnabled}")
        val gamePath = getExplicitCurrentDeviceGamePath(gameDef, deviceId)
        sb.appendLine("Android game path: ${gamePath.ifBlank { "(empty)" }}")
        sb.appendLine()
        sb.appendLine("Android save paths:")
        val paths = getExplicitCurrentDevicePaths(gameDef, deviceId)
        if (paths.isEmpty()) {
            sb.appendLine("- none")
        } else {
            paths.forEach { p ->
                sb.appendLine("- unit ${p.saveUnitId} [${p.unitType.name}] ${p.path.ifBlank { "(empty)" }}")
            }
        }
        return sb.toString().trimEnd()
    }

    private fun buildCloudDetails(status: GameCloudStatus, backups: List<CloudGameBackup>): String {
        val sb = StringBuilder()
        sb.appendLine("Game: ${status.gameName}")
        sb.appendLine("Cloud root: ${status.rootKey}")
        sb.appendLine("Cloud data exists: ${status.isAvailable}")
        sb.appendLine("Current head: ${status.currentHead ?: "(none)"}")
        sb.appendLine("Backup count: ${status.backupCount}")
        sb.appendLine()
        sb.appendLine("Recent backups:")
        if (backups.isEmpty()) {
            sb.appendLine("- none")
        } else {
            backups.take(8).forEach { backup ->
                sb.append("- ${backup.date}")
                    .append(" | ${formatSize(backup.size)}")
                    .append(" | ${backup.deviceId}")
                if (backup.isCurrentDeviceHead) sb.append(" | current")
                sb.appendLine()
            }
        }
        return sb.toString().trimEnd()
    }

    private fun buildSaveUnitTargets(gameDef: GameDefinition?): List<SaveUnitTargetOption> {
        val targets = mutableListOf<SaveUnitTargetOption>()
        gameDef?.save_paths?.sortedBy { it.id }?.forEach { unit ->
            targets.add(SaveUnitTargetOption(
                saveUnitId = unit.id,
                unitType = unit.unit_type,
                isNew = false,
                label = "Unit ${unit.id} [${unit.unit_type.name}]"
            ))
        }
        targets.add(SaveUnitTargetOption(null, SaveUnitType.Folder, true, "New folder save unit"))
        targets.add(SaveUnitTargetOption(null, SaveUnitType.File, true, "New file save unit"))
        return targets
    }

    private data class CurrentDevicePathInfo(
        val saveUnitId: Int,
        val unitType: SaveUnitType,
        val path: String
    )

    private fun getExplicitCurrentDevicePaths(gameDef: GameDefinition?, deviceId: String): List<CurrentDevicePathInfo> {
        if (gameDef == null || deviceId.isBlank()) return emptyList()
        return gameDef.save_paths
            .filter { unit ->
                unit.paths[deviceId]?.isNotBlank() == true
            }
            .map { unit ->
                CurrentDevicePathInfo(unit.id, unit.unit_type, unit.paths[deviceId]!!)
            }
            .sortedBy { it.saveUnitId }
    }

    private fun getExplicitCurrentDevicePath(gameDef: GameDefinition?, deviceId: String): CurrentDevicePathInfo? {
        return getExplicitCurrentDevicePaths(gameDef, deviceId).firstOrNull()
    }

    private fun getExplicitCurrentDeviceGamePath(gameDef: GameDefinition?, deviceId: String): String {
        if (gameDef == null || deviceId.isBlank()) return ""
        return gameDef.game_paths[deviceId] ?: ""
    }

    private fun isSupportedAndroidPath(path: String): Boolean {
        if (path.isBlank()) return false
        return path.startsWith("/") || path.startsWith("content://", ignoreCase = true)
    }

    private fun formatSize(size: Long): String {
        return when {
            size < 1024 -> "$size B"
            size < 1024 * 1024 -> "${"%.1f".format(size / 1024.0)} KB"
            else -> "${"%.1f".format(size / 1024.0 / 1024.0)} MB"
        }
    }
}

data class GameListItem(
    val name: String,
    val cloudSyncEnabled: Boolean,
    val saveUnits: List<SaveUnitSnapshot>,
    val isFavorite: Boolean
)

data class SaveUnitTargetOption(
    val saveUnitId: Int?,
    val unitType: SaveUnitType,
    val isNew: Boolean,
    val label: String
)
