package com.eflay.gamesavemanager.ui.component

import androidx.compose.foundation.layout.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.eflay.gamesavemanager.model.SaveUnitType
import com.eflay.gamesavemanager.ui.theme.*

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun AddGamePanel(
    gameName: String,
    savePath: String,
    gamePath: String,
    selectedType: SaveUnitType,
    onGameNameChange: (String) -> Unit,
    onSavePathChange: (String) -> Unit,
    onGamePathChange: (String) -> Unit,
    onTypeChange: (SaveUnitType) -> Unit,
    onPickFile: () -> Unit,
    onPickFolder: () -> Unit,
    onCreate: () -> Unit,
    onClose: () -> Unit
) {
    var typeExpanded by remember { mutableStateOf(false) }
    val typeOptions = listOf(SaveUnitType.Folder, SaveUnitType.File)

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
                text = "Add game",
                fontSize = 20.sp,
                fontWeight = FontWeight.Bold
            )

            OutlinedTextField(
                value = gameName,
                onValueChange = onGameNameChange,
                label = { Text("Game name") },
                modifier = Modifier.fillMaxWidth(),
                singleLine = true
            )

            // Save unit type dropdown
            ExposedDropdownMenuBox(
                expanded = typeExpanded,
                onExpandedChange = { typeExpanded = it }
            ) {
                OutlinedTextField(
                    value = selectedType.name,
                    onValueChange = {},
                    readOnly = true,
                    label = { Text("Save unit type") },
                    trailingIcon = { ExposedDropdownMenuDefaults.TrailingIcon(expanded = typeExpanded) },
                    modifier = Modifier.fillMaxWidth().menuAnchor()
                )
                ExposedDropdownMenu(
                    expanded = typeExpanded,
                    onDismissRequest = { typeExpanded = false }
                ) {
                    typeOptions.forEach { type ->
                        DropdownMenuItem(
                            text = { Text(type.name) },
                            onClick = {
                                onTypeChange(type)
                                typeExpanded = false
                            }
                        )
                    }
                }
            }

            OutlinedTextField(
                value = savePath,
                onValueChange = onSavePathChange,
                label = { Text("Save file path or save folder path") },
                modifier = Modifier.fillMaxWidth(),
                singleLine = true
            )

            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.spacedBy(10.dp)
            ) {
                Button(
                    onClick = {
                        onTypeChange(SaveUnitType.File)
                        onPickFile()
                    },
                    modifier = Modifier.weight(1f)
                ) {
                    Text("Pick file")
                }
                Button(
                    onClick = {
                        onTypeChange(SaveUnitType.Folder)
                        onPickFolder()
                    },
                    modifier = Modifier.weight(1f)
                ) {
                    Text("Pick folder")
                }
            }

            OutlinedTextField(
                value = gamePath,
                onValueChange = onGamePathChange,
                label = { Text("Game launch path (optional)") },
                modifier = Modifier.fillMaxWidth(),
                singleLine = true
            )

            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.spacedBy(10.dp)
            ) {
                Button(onClick = onCreate, modifier = Modifier.weight(1f)) {
                    Text("Create")
                }
                OutlinedButton(onClick = onClose, modifier = Modifier.weight(1f)) {
                    Text("Close")
                }
            }

            Text(
                text = "First Android pass keeps file and folder sync only. Switch emulator zip sync is tracked in docs/ANDROID_MAUI_TODO.md.",
                fontSize = 12.sp,
                color = MutedTextColor
            )
        }
    }
}
