package com.eflay.gamesavemanager.ui.component

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material3.*
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.eflay.gamesavemanager.model.SaveUnitType
import com.eflay.gamesavemanager.ui.theme.*
import com.eflay.gamesavemanager.viewmodel.GameListItem

@Composable
fun AllGamesList(
    games: List<GameListItem>,
    selectedGame: GameListItem?,
    onSelectGame: (GameListItem) -> Unit,
    onToggleFavorite: (GameListItem) -> Unit
) {
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
                text = "All games",
                fontSize = 20.sp,
                fontWeight = FontWeight.Bold
            )

            LazyColumn(
                modifier = Modifier.heightIn(max = 300.dp)
            ) {
                items(games, key = { it.name }) { game ->
                    val isSelected = game.name == selectedGame?.name
                    Card(
                        modifier = Modifier
                            .fillMaxWidth()
                            .padding(vertical = 4.dp)
                            .clickable { onSelectGame(game) },
                        colors = CardDefaults.cardColors(
                            containerColor = if (isSelected) FavoriteColor else PanelColor
                        ),
                        elevation = CardDefaults.cardElevation(defaultElevation = 0.dp)
                    ) {
                        Row(
                            modifier = Modifier
                                .fillMaxWidth()
                                .padding(horizontal = 8.dp, vertical = 8.dp),
                            horizontalArrangement = Arrangement.SpaceBetween,
                            verticalAlignment = Alignment.CenterVertically
                        ) {
                            Column(modifier = Modifier.weight(1f)) {
                                Text(
                                    text = game.name,
                                    fontWeight = FontWeight.Bold,
                                    fontSize = 14.sp
                                )
                                Text(
                                    text = "${if (game.cloudSyncEnabled) "Cloud" else "Local"} | " +
                                        "${game.saveUnits.count { it.unitType == SaveUnitType.Folder }} folder, " +
                                        "${game.saveUnits.count { it.unitType == SaveUnitType.File }} file",
                                    fontSize = 12.sp,
                                    color = MutedTextColor,
                                    maxLines = 1,
                                    overflow = TextOverflow.Ellipsis
                                )
                            }
                            TextButton(onClick = { onToggleFavorite(game) }) {
                                Text(
                                    text = if (game.isFavorite) "Unfavorite" else "Favorite",
                                    fontSize = 12.sp
                                )
                            }
                        }
                    }
                }
            }
        }
    }
}
