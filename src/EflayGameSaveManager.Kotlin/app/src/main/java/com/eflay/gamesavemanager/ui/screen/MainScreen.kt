package com.eflay.gamesavemanager.ui.screen

import android.net.Uri
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import androidx.lifecycle.viewmodel.compose.viewModel
import com.eflay.gamesavemanager.model.SaveUnitType
import com.eflay.gamesavemanager.service.StoragePathResolver
import com.eflay.gamesavemanager.ui.component.*
import com.eflay.gamesavemanager.ui.theme.*
import com.eflay.gamesavemanager.viewmodel.MainViewModel

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun MainScreen(viewModel: MainViewModel = viewModel()) {
    val state by viewModel.uiState.collectAsState()
    val context = LocalContext.current

    // Resolve content:// URI to real path, or show error and return null
    fun resolvePickResult(uri: Uri?): String? {
        if (uri == null) return null
        val realPath = StoragePathResolver.resolveToRealPath(context, uri)
        if (realPath != null && realPath.startsWith("/")) return realPath
        // Resolution failed — tell user to type path manually
        viewModel.setStatusMessage(
            "Cannot resolve to a real path. Please type the path manually. " +
            "Hint: game save folders are usually under /storage/emulated/0/"
        )
        return null
    }

    // File picker launcher
    val filePickerLauncher = rememberLauncherForActivityResult(
        contract = ActivityResultContracts.OpenDocument()
    ) { uri: Uri? ->
        resolvePickResult(uri)?.let { path ->
            viewModel.updateAddSavePath(path)
            viewModel.updateSelectedSaveUnitType(SaveUnitType.File)
        }
    }

    // Folder picker launcher
    val folderPickerLauncher = rememberLauncherForActivityResult(
        contract = ActivityResultContracts.OpenDocumentTree()
    ) { uri: Uri? ->
        resolvePickResult(uri)?.let { path ->
            viewModel.updateAddSavePath(path)
            viewModel.updateSelectedSaveUnitType(SaveUnitType.Folder)
        }
    }

    // File picker for edit path
    val editFilePickerLauncher = rememberLauncherForActivityResult(
        contract = ActivityResultContracts.OpenDocument()
    ) { uri: Uri? ->
        resolvePickResult(uri)?.let { path ->
            viewModel.updateEditSavePath(path)
        }
    }

    // Folder picker for edit path
    val editFolderPickerLauncher = rememberLauncherForActivityResult(
        contract = ActivityResultContracts.OpenDocumentTree()
    ) { uri: Uri? ->
        resolvePickResult(uri)?.let { path ->
            viewModel.updateEditSavePath(path)
        }
    }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text("Game Save Sync") },
                colors = TopAppBarDefaults.topAppBarColors(
                    containerColor = PanelColor
                )
            )
        }
    ) { padding ->
        Column(
            modifier = Modifier
                .fillMaxSize()
                .padding(padding)
                .verticalScroll(rememberScrollState())
                .padding(18.dp),
            verticalArrangement = Arrangement.spacedBy(14.dp)
        ) {
            HeaderPanel(
                currentDeviceSummary = state.currentDeviceSummary,
                configPathSummary = state.configPathSummary,
                statusMessage = state.statusMessage,
                shizukuStatus = state.shizukuStatus,
                onReload = { viewModel.reload() },
                onToggleAddGame = { viewModel.toggleAddGame() }
            )

            if (state.isAddGameVisible) {
                AddGamePanel(
                    gameName = state.addGameName,
                    savePath = state.addSavePath,
                    gamePath = state.addGamePath,
                    selectedType = state.selectedSaveUnitType,
                    onGameNameChange = { viewModel.updateAddGameName(it) },
                    onSavePathChange = { viewModel.updateAddSavePath(it) },
                    onGamePathChange = { viewModel.updateAddGamePath(it) },
                    onTypeChange = { viewModel.updateSelectedSaveUnitType(it) },
                    onPickFile = { filePickerLauncher.launch(arrayOf("*/*")) },
                    onPickFolder = { folderPickerLauncher.launch(null) },
                    onCreate = { viewModel.addGame() },
                    onClose = { viewModel.toggleAddGame() }
                )
            }

            FavoritesBar(
                favorites = state.favoriteGames,
                hasNoFavorites = state.hasNoFavoriteGames,
                onSelectGame = { viewModel.selectGame(it) }
            )

            AllGamesList(
                games = state.games,
                selectedGame = state.selectedGame,
                onSelectGame = { viewModel.selectGame(it) },
                onToggleFavorite = { viewModel.toggleFavorite(it) }
            )

            if (state.selectedGame != null) {
                GameDetailPanel(
                    title = state.selectedGameTitle,
                    summary = state.selectedGameSummary,
                    details = state.selectedGameDetails,
                    cloudDetails = state.cloudDetails,
                    saveUnitTargets = state.saveUnitTargets,
                    selectedTarget = state.selectedSaveUnitTarget,
                    editSavePath = state.editSavePath,
                    editGamePath = state.editGamePath,
                    onTargetChange = { viewModel.updateSelectedSaveUnitTarget(it) },
                    onEditSavePathChange = { viewModel.updateEditSavePath(it) },
                    onEditGamePathChange = { viewModel.updateEditGamePath(it) },
                    onPickEditFile = { editFilePickerLauncher.launch(arrayOf("*/*")) },
                    onPickEditFolder = { editFolderPickerLauncher.launch(null) },
                    onSavePaths = { viewModel.saveSelectedGamePaths() },
                    onRefreshCloud = { viewModel.refreshCloud() },
                    onUploadCurrent = { viewModel.uploadCurrent() },
                    onRestoreCurrent = { viewModel.restoreCurrent() }
                )
            }
        }
    }
}
