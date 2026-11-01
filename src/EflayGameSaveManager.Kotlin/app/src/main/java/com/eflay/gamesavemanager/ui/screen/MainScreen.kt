package com.eflay.gamesavemanager.ui.screen

import android.net.Uri
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.viewmodel.compose.viewModel
import com.eflay.gamesavemanager.model.SaveUnitType
import com.eflay.gamesavemanager.service.StoragePathResolver
import com.eflay.gamesavemanager.ui.component.*
import com.eflay.gamesavemanager.ui.theme.*
import com.eflay.gamesavemanager.viewmodel.GameListItem
import com.eflay.gamesavemanager.viewmodel.MainViewModel
import com.eflay.gamesavemanager.viewmodel.CloudBackupRow
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch

private enum class Tab(val label: String, val icon: @Composable () -> Unit) {
    Favorites("Favorites", { Icon(Icons.Default.Star, null) }),
    AllGames("All Games", { Icon(Icons.Default.Games, null) }),
    Config("Config", { Icon(Icons.Default.Settings, null) });
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun MainScreen(viewModel: MainViewModel = viewModel()) {
    val state by viewModel.uiState.collectAsState()
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    val drawerState = rememberDrawerState(DrawerValue.Closed)
    var selectedTab by remember { mutableStateOf(Tab.Favorites) }

    // Resolve content:// URI to real path
    fun resolvePickResult(uri: Uri?): String? {
        if (uri == null) return null
        val realPath = StoragePathResolver.resolveToRealPath(context, uri)
        if (realPath != null && realPath.startsWith("/")) return realPath
        viewModel.setStatusMessage("Cannot resolve to a real path. Please type the path manually.")
        return null
    }

    // --- Add game pickers ---
    val filePickerLauncher = rememberLauncherForActivityResult(ActivityResultContracts.OpenDocument()) { uri ->
        resolvePickResult(uri)?.let {
            viewModel.updateAddSavePath(it)
            viewModel.updateSelectedSaveUnitType(SaveUnitType.File)
        }
    }
    val folderPickerLauncher = rememberLauncherForActivityResult(ActivityResultContracts.OpenDocumentTree()) { uri ->
        resolvePickResult(uri)?.let {
            viewModel.updateAddSavePath(it)
            viewModel.updateSelectedSaveUnitType(SaveUnitType.Folder)
        }
    }
    val editFilePickerLauncher = rememberLauncherForActivityResult(ActivityResultContracts.OpenDocument()) { uri ->
        resolvePickResult(uri)?.let { viewModel.updateEditSavePath(it) }
    }
    val editFolderPickerLauncher = rememberLauncherForActivityResult(ActivityResultContracts.OpenDocumentTree()) { uri ->
        resolvePickResult(uri)?.let { viewModel.updateEditSavePath(it) }
    }

    // --- Zip mode pickers ---
    val zipUploadPickerLauncher = rememberLauncherForActivityResult(ActivityResultContracts.OpenDocument()) { uri ->
        val path = uri?.let { StoragePathResolver.resolveToRealPath(context, it) } ?: uri?.toString() ?: ""
        viewModel.uploadCurrentWithZipPath(path)
    }
    val zipRestorePickerLauncher = rememberLauncherForActivityResult(ActivityResultContracts.CreateDocument("application/zip")) { uri ->
        val path = uri?.let { StoragePathResolver.resolveToRealPath(context, it) } ?: uri?.toString() ?: ""
        viewModel.restoreCurrentWithZipPath(path)
    }

    // Trigger zip pickers
    LaunchedEffect(state.showZipUploadPicker) {
        if (state.showZipUploadPicker) { delay(100); zipUploadPickerLauncher.launch(arrayOf("application/zip", "*/*")) }
    }
    LaunchedEffect(state.showZipRestorePicker) {
        if (state.showZipRestorePicker) {
            delay(100)
            val name = state.selectedGame?.name?.replace(Regex("""[<>:"/\\|?*]"""), "_") ?: "save"
            zipRestorePickerLauncher.launch("$name.zip")
        }
    }

    // --- Config import picker ---
    val configImportLauncher = rememberLauncherForActivityResult(ActivityResultContracts.OpenDocument()) { uri ->
        uri?.let { viewModel.importConfigFile(it) }
    }

    ModalNavigationDrawer(
        drawerState = drawerState,
        drawerContent = {
            ModalDrawerSheet {
                Spacer(Modifier.height(16.dp))
                Text("Game Save Manager", modifier = Modifier.padding(16.dp), fontWeight = FontWeight.Bold, fontSize = 18.sp)
                HorizontalDivider()
                Tab.entries.forEach { tab ->
                    NavigationDrawerItem(
                        icon = tab.icon,
                        label = { Text(tab.label) },
                        selected = selectedTab == tab,
                        onClick = {
                            selectedTab = tab
                            scope.launch { drawerState.close() }
                        }
                    )
                }
                HorizontalDivider(Modifier.padding(vertical = 8.dp))
                Text(state.shizukuStatus, fontSize = 11.sp, color = MutedTextColor, modifier = Modifier.padding(horizontal = 16.dp))
                Text(state.currentDeviceSummary, fontSize = 11.sp, color = MutedTextColor, modifier = Modifier.padding(horizontal = 16.dp))
            }
        }
    ) {
        Scaffold(
            topBar = {
                TopAppBar(
                    title = { Text(selectedTab.label) },
                    navigationIcon = {
                        IconButton(onClick = { scope.launch { drawerState.open() } }) {
                            Icon(Icons.Default.Menu, "Menu")
                        }
                    },
                    colors = TopAppBarDefaults.topAppBarColors(containerColor = PanelColor)
                )
            }
        ) { padding ->
            Column(
                modifier = Modifier.fillMaxSize().padding(padding)
            ) {
                // Status bar
                Text(
                    state.statusMessage, fontSize = 13.sp, color = AccentColor,
                    modifier = Modifier.padding(horizontal = 18.dp, vertical = 6.dp)
                )
                HorizontalDivider()

                when (selectedTab) {
                    Tab.Favorites -> FavoritesPage(viewModel, state)
                    Tab.AllGames -> AllGamesPage(
                        viewModel, state,
                        filePickerLauncher, folderPickerLauncher,
                        editFilePickerLauncher, editFolderPickerLauncher
                    )
                    Tab.Config -> ConfigEditorPage(viewModel, state, configImportLauncher)
                }
            }
        }
    }
}

// ========= Favorites Page =========
@Composable
private fun FavoritesPage(viewModel: MainViewModel, state: MainViewModel.UiState) {
    if (state.favoriteGames.isEmpty()) {
        Box(Modifier.fillMaxSize().padding(32.dp), contentAlignment = Alignment.Center) {
            Text("No favorites yet. Star games in the All Games tab.", color = MutedTextColor)
        }
        return
    }
    LazyColumn(Modifier.fillMaxSize().padding(horizontal = 14.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
        item { Spacer(Modifier.height(4.dp)) }
        items(state.favoriteGames, key = { it.name }) { game ->
            FavoriteGameCard(viewModel, game, state)
        }
    }
}

@Composable
private fun FavoriteGameCard(viewModel: MainViewModel, game: GameListItem, state: MainViewModel.UiState) {
    Card(
        Modifier.fillMaxWidth().clickable { viewModel.selectGame(game) },
        colors = CardDefaults.cardColors(containerColor = if (state.selectedGame?.name == game.name) FavoriteColor else PanelColor),
        elevation = CardDefaults.cardElevation(0.dp)
    ) {
        Row(Modifier.padding(12.dp), verticalAlignment = Alignment.CenterVertically) {
            Column(Modifier.weight(1f)) {
                Text(game.name, fontWeight = FontWeight.Bold, fontSize = 15.sp)
                Text(
                    "${if (game.cloudSyncEnabled) "Cloud" else "Local"} | " +
                    "${game.saveUnits.count { it.unitType == SaveUnitType.Folder }} folder, " +
                    "${game.saveUnits.count { it.unitType == SaveUnitType.File }} file" +
                    if (game.saveUnits.any { it.unitType == SaveUnitType.Zip }) " + Zip" else "",
                    fontSize = 12.sp, color = MutedTextColor
                )
            }
            IconButton(onClick = { viewModel.toggleFavorite(game) }) {
                Icon(Icons.Default.Star, "Unfavorite", tint = AccentColor)
            }
        }
        // Quick actions
        if (state.selectedGame?.name == game.name) {
            GameDetailCompact(viewModel, state)
        }
    }
}

@Composable
private fun GameDetailCompact(viewModel: MainViewModel, state: MainViewModel.UiState) {
    Column(Modifier.padding(horizontal = 12.dp, vertical = 8.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
        Text(state.cloudDetails.ifBlank { "Cloud status not loaded." }, fontSize = 11.sp, lineHeight = 15.sp)
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            OutlinedButton(onClick = { viewModel.refreshCloud() }, Modifier.weight(1f)) { Text("Refresh", fontSize = 12.sp) }
            Button(onClick = { viewModel.uploadCurrent() }, Modifier.weight(1f)) { Text("Upload", fontSize = 12.sp) }
            Button(onClick = { viewModel.restoreCurrent() }, Modifier.weight(1f)) { Text("Restore", fontSize = 12.sp) }
        }
        CloudBackupPanel(viewModel, state)
        NfsEditorPanel(viewModel, state)
    }
}

// ========= All Games Page =========
@Composable
private fun AllGamesPage(
    viewModel: MainViewModel, state: MainViewModel.UiState,
    filePickerLauncher: androidx.activity.result.ActivityResultLauncher<Array<String>>,
    folderPickerLauncher: androidx.activity.result.ActivityResultLauncher<Uri?>,
    editFilePickerLauncher: androidx.activity.result.ActivityResultLauncher<Array<String>>,
    editFolderPickerLauncher: androidx.activity.result.ActivityResultLauncher<Uri?>
) {
    Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(14.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {

        // Header panel
        HeaderPanel(
            currentDeviceSummary = state.currentDeviceSummary,
            configPathSummary = state.configPathSummary,
            statusMessage = "",
            shizukuStatus = state.shizukuStatus,
            onReload = { viewModel.reload() },
            onToggleAddGame = { viewModel.toggleAddGame() }
        )

        // Add game panel
        if (state.isAddGameVisible) {
            AddGamePanel(
                gameName = state.addGameName, savePath = state.addSavePath, gamePath = state.addGamePath,
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

        // All games list
        AllGamesList(
            games = state.games, selectedGame = state.selectedGame,
            onSelectGame = { viewModel.selectGame(it) },
            onToggleFavorite = { viewModel.toggleFavorite(it) }
        )

        // Game detail
        if (state.selectedGame != null) {
            GameDetailPanel(
                title = state.selectedGameTitle, summary = state.selectedGameSummary,
                details = state.selectedGameDetails, cloudDetails = state.cloudDetails,
                saveUnitTargets = state.saveUnitTargets, selectedTarget = state.selectedSaveUnitTarget,
                editSavePath = state.editSavePath, editGamePath = state.editGamePath,
                editExcludePatterns = state.editExcludePatterns,
                editContentsOnly = state.editContentsOnly,
                onTargetChange = { viewModel.updateSelectedSaveUnitTarget(it) },
                onEditSavePathChange = { viewModel.updateEditSavePath(it) },
                onEditGamePathChange = { viewModel.updateEditGamePath(it) },
                onEditExcludePatternsChange = { viewModel.updateEditExcludePatterns(it) },
                onEditContentsOnlyChange = { viewModel.updateEditContentsOnly(it) },
                onPickEditFile = { editFilePickerLauncher.launch(arrayOf("*/*")) },
                onPickEditFolder = { editFolderPickerLauncher.launch(null) },
                onSavePaths = { viewModel.saveSelectedGamePaths() },
                onRefreshCloud = { viewModel.refreshCloud() },
                onUploadCurrent = { viewModel.uploadCurrent() },
                onRestoreCurrent = { viewModel.restoreCurrent() }
            )
            CloudBackupPanel(viewModel, state)
            NfsEditorPanel(viewModel, state)
        }
    }
}

@Composable
private fun NfsEditorPanel(viewModel: MainViewModel, state: MainViewModel.UiState) {
    if (!state.isNfs9) return
    var expanded by remember { mutableStateOf(false) }
    Card(Modifier.fillMaxWidth(), colors = CardDefaults.cardColors(containerColor = CloudDetailBg)) {
        Column(Modifier.padding(12.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            Text("极品飞车9 · 个人存档修改", fontWeight = FontWeight.Bold, fontSize = 16.sp)
            Text("请先退出游戏。修改档案名、金钱、总赏金或当前黑名单条件；写入前会备份。", fontSize = 12.sp)
            Button(onClick = viewModel::findNfsProfiles, enabled = !state.isBusy) { Text("查找个人存档") }
            Box {
                OutlinedButton(onClick = { expanded = true }, enabled = state.nfsProfiles.isNotEmpty()) { Text(state.selectedNfsProfile?.name ?: "选择个人存档") }
                DropdownMenu(expanded = expanded, onDismissRequest = { expanded = false }) {
                    state.nfsProfiles.forEach { profile -> DropdownMenuItem(text = { Text(profile.name) }, onClick = { viewModel.selectNfsProfile(profile); expanded = false }) }
                }
            }
            Button(onClick = viewModel::readNfsProfile, enabled = state.selectedNfsProfile != null && !state.isBusy) { Text("读取所选存档") }
            if (state.nfsLoaded) {
                OutlinedTextField(value = state.nfsAlias, onValueChange = viewModel::updateNfsAlias, label = { Text("档案名（ASCII）") }, modifier = Modifier.fillMaxWidth(), singleLine = true)
                OutlinedTextField(value = state.nfsMoney, onValueChange = viewModel::updateNfsMoney, label = { Text("金钱") }, modifier = Modifier.fillMaxWidth(), singleLine = true)
                OutlinedTextField(value = state.nfsBounty, onValueChange = viewModel::updateNfsBounty, label = { Text("个人总赏金") }, modifier = Modifier.fillMaxWidth(), singleLine = true)
                Text("当前黑名单阶段：${state.nfsStage}")
                Button(onClick = viewModel::saveNfsProfile, enabled = !state.isBusy) { Text("保存修改并备份") }
                Button(onClick = viewModel::makeNfsBossReady, enabled = !state.isBusy) { Text("满足比赛和里程碑条件") }
            }
            Text(state.nfsStatus, fontSize = 12.sp)
        }
    }
}

@Composable
private fun CloudBackupPanel(viewModel: MainViewModel, state: MainViewModel.UiState) {
    if (state.cloudBackups.isEmpty()) return
    Card(Modifier.fillMaxWidth(), colors = CardDefaults.cardColors(containerColor = PanelColor)) {
        Column(Modifier.padding(12.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            Text("云端存档", fontWeight = FontWeight.Bold, fontSize = 16.sp)
            state.cloudBackups.forEach { row ->
                val selected = state.selectedCloudBackup == row
                Surface(Modifier.fillMaxWidth().clickable { viewModel.selectCloudBackup(row) }, color = if (selected) FavoriteColor else CloudDetailBg, shape = MaterialTheme.shapes.medium) {
                    Column(Modifier.padding(10.dp)) {
                        Text(row.backup.date, fontWeight = FontWeight.SemiBold)
                        val bytes = row.backup.size
                        val size = when { bytes < 1024 -> "$bytes B"; bytes < 1048576 -> "${"%.1f".format(bytes / 1024.0)} KB"; else -> "${"%.1f".format(bytes / 1048576.0)} MB" }
                        Text("${row.deviceName} · $size" + if (row.backup.isCurrentDeviceHead) " · 本设备最新" else "", fontSize = 12.sp, color = MutedTextColor)
                        if (row.backup.describe.isNotBlank()) Text("描述：${row.backup.describe}", fontSize = 13.sp)
                    }
                }
            }
            if (state.selectedCloudBackup != null) {
                OutlinedTextField(value = state.cloudBackupDescription, onValueChange = viewModel::updateCloudBackupDescription, label = { Text("存档描述（留空可清除）") }, modifier = Modifier.fillMaxWidth())
                Button(onClick = viewModel::saveCloudBackupDescription, enabled = !state.isBusy) { Text("保存描述") }
            }
        }
    }
}

// ========= Config Editor Page =========
@Composable
private fun ConfigEditorPage(
    viewModel: MainViewModel, state: MainViewModel.UiState,
    importLauncher: androidx.activity.result.ActivityResultLauncher<Array<String>>
) {
    var rawJson by remember { mutableStateOf("") }
    var loaded by remember { mutableStateOf(false) }

    // Load config content once
    LaunchedEffect(state.configRevision) {
        rawJson = viewModel.loadConfigRaw()
        loaded = true
    }

    Column(Modifier.fillMaxSize().padding(14.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
        Text("Edit GameSaveManager.config.json", fontWeight = FontWeight.Bold, fontSize = 16.sp)
        Text(state.configPathSummary, fontSize = 12.sp, color = MutedTextColor)
        Text(state.statusMessage, fontSize = 12.sp, color = AccentColor)
        OutlinedButton(onClick = { viewModel.importMauiConfiguration() }, modifier = Modifier.fillMaxWidth()) {
            Text("从已安装的 MAUI 版导入当前配置")
        }

        if (!loaded) {
            CircularProgressIndicator(Modifier.align(Alignment.CenterHorizontally))
        } else {
            OutlinedTextField(
                value = rawJson, onValueChange = { rawJson = it },
                modifier = Modifier.fillMaxWidth().weight(1f),
                textStyle = androidx.compose.ui.text.TextStyle(fontSize = 11.sp, lineHeight = 15.sp)
            )
        }

        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            Button(onClick = { viewModel.saveConfigRaw(rawJson) }, Modifier.weight(1f)) {
                Text("Save")
            }
            OutlinedButton(onClick = { importLauncher.launch(arrayOf("application/json", "*/*")) }, Modifier.weight(1f)) {
                Text("Import...")
            }
            OutlinedButton(onClick = { viewModel.reload(); rawJson = viewModel.loadConfigRaw() }, Modifier.weight(1f)) {
                Text("Reload")
            }
        }
    }
}
