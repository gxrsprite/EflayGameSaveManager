package com.eflay.gamesavemanager.ui.component

import androidx.compose.foundation.layout.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.eflay.gamesavemanager.ui.theme.*
import com.eflay.gamesavemanager.viewmodel.SaveUnitTargetOption

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun GameDetailPanel(
    title: String,
    summary: String,
    details: String,
    cloudDetails: String,
    saveUnitTargets: List<SaveUnitTargetOption>,
    selectedTarget: SaveUnitTargetOption?,
    editSavePath: String,
    editGamePath: String,
    onTargetChange: (SaveUnitTargetOption) -> Unit,
    onEditSavePathChange: (String) -> Unit,
    onEditGamePathChange: (String) -> Unit,
    onPickEditFile: () -> Unit,
    onPickEditFolder: () -> Unit,
    onSavePaths: () -> Unit,
    onRefreshCloud: () -> Unit,
    onUploadCurrent: () -> Unit,
    onRestoreCurrent: () -> Unit
) {
    var targetExpanded by remember { mutableStateOf(false) }

    Card(
        modifier = Modifier.fillMaxWidth(),
        colors = CardDefaults.cardColors(containerColor = PanelColor),
        elevation = CardDefaults.cardElevation(defaultElevation = 0.dp)
    ) {
        Column(
            modifier = Modifier.padding(14.dp),
            verticalArrangement = Arrangement.spacedBy(10.dp)
        ) {
            Text(
                text = title,
                fontSize = 20.sp,
                fontWeight = FontWeight.Bold
            )
            Text(
                text = summary,
                fontSize = 12.sp,
                color = MutedTextColor
            )

            // Game details
            Card(
                modifier = Modifier.fillMaxWidth(),
                colors = CardDefaults.cardColors(containerColor = GameDetailBg),
                elevation = CardDefaults.cardElevation(defaultElevation = 0.dp)
            ) {
                Text(
                    text = details,
                    fontSize = 12.sp,
                    modifier = Modifier.padding(12.dp),
                    lineHeight = 18.sp
                )
            }

            // Android paths editor
            Card(
                modifier = Modifier.fillMaxWidth(),
                colors = CardDefaults.cardColors(containerColor = DetailEditorBg),
                elevation = CardDefaults.cardElevation(defaultElevation = 0.dp)
            ) {
                Column(
                    modifier = Modifier.padding(12.dp),
                    verticalArrangement = Arrangement.spacedBy(10.dp)
                ) {
                    Text(
                        text = "Android paths for this game",
                        fontSize = 16.sp,
                        fontWeight = FontWeight.Bold
                    )

                    // Save unit target picker
                    ExposedDropdownMenuBox(
                        expanded = targetExpanded,
                        onExpandedChange = { targetExpanded = it }
                    ) {
                        OutlinedTextField(
                            value = selectedTarget?.label ?: "Select save unit",
                            onValueChange = {},
                            readOnly = true,
                            label = { Text("Save unit target") },
                            trailingIcon = { ExposedDropdownMenuDefaults.TrailingIcon(expanded = targetExpanded) },
                            modifier = Modifier.fillMaxWidth().menuAnchor()
                        )
                        ExposedDropdownMenu(
                            expanded = targetExpanded,
                            onDismissRequest = { targetExpanded = false }
                        ) {
                            saveUnitTargets.forEach { option ->
                                DropdownMenuItem(
                                    text = { Text(option.label) },
                                    onClick = {
                                        onTargetChange(option)
                                        targetExpanded = false
                                    }
                                )
                            }
                        }
                    }

                    OutlinedTextField(
                        value = editSavePath,
                        onValueChange = onEditSavePathChange,
                        label = { Text("Android save file path or folder path") },
                        modifier = Modifier.fillMaxWidth(),
                        singleLine = true
                    )

                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        horizontalArrangement = Arrangement.spacedBy(10.dp)
                    ) {
                        Button(onClick = onPickEditFile, modifier = Modifier.weight(1f)) {
                            Text("Pick file", fontSize = 12.sp)
                        }
                        Button(onClick = onPickEditFolder, modifier = Modifier.weight(1f)) {
                            Text("Pick folder", fontSize = 12.sp)
                        }
                    }

                    OutlinedTextField(
                        value = editGamePath,
                        onValueChange = onEditGamePathChange,
                        label = { Text("Android game launch path (optional)") },
                        modifier = Modifier.fillMaxWidth(),
                        singleLine = true
                    )

                    Button(
                        onClick = onSavePaths,
                        modifier = Modifier.fillMaxWidth()
                    ) {
                        Text("Save Android paths")
                    }
                }
            }

            // Cloud actions
            Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                Button(
                    onClick = onRefreshCloud,
                    modifier = Modifier.fillMaxWidth()
                ) {
                    Text("Refresh cloud")
                }
                Button(
                    onClick = onUploadCurrent,
                    modifier = Modifier.fillMaxWidth()
                ) {
                    Text("Upload current")
                }
                Button(
                    onClick = onRestoreCurrent,
                    modifier = Modifier.fillMaxWidth()
                ) {
                    Text("Restore current")
                }
            }

            // Cloud details
            Card(
                modifier = Modifier.fillMaxWidth(),
                colors = CardDefaults.cardColors(containerColor = CloudDetailBg),
                elevation = CardDefaults.cardElevation(defaultElevation = 0.dp)
            ) {
                Text(
                    text = cloudDetails,
                    fontSize = 12.sp,
                    modifier = Modifier.padding(12.dp),
                    lineHeight = 18.sp
                )
            }
        }
    }
}
