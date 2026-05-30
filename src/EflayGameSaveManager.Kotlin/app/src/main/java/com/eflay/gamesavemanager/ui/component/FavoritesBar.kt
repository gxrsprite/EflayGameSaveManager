package com.eflay.gamesavemanager.ui.component

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.items
import androidx.compose.material3.*
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.eflay.gamesavemanager.ui.theme.*
import com.eflay.gamesavemanager.viewmodel.GameListItem

@Composable
fun FavoritesBar(
    favorites: List<GameListItem>,
    hasNoFavorites: Boolean,
    onSelectGame: (GameListItem) -> Unit
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
                text = "Favorites",
                fontSize = 20.sp,
                fontWeight = FontWeight.Bold
            )
            Text(
                text = "Games you care about on Android can stay here for quick cloud sync.",
                fontSize = 12.sp,
                color = MutedTextColor
            )

            if (favorites.isEmpty()) {
                Text(
                    text = "No favorites yet. Star the games you want quick access to.",
                    fontSize = 12.sp,
                    color = MutedTextColor
                )
            } else {
                LazyRow(
                    horizontalArrangement = Arrangement.spacedBy(8.dp)
                ) {
                    items(favorites, key = { it.name }) { game ->
                        Card(
                            modifier = Modifier.width(180.dp),
                            colors = CardDefaults.cardColors(containerColor = FavoriteColor),
                            elevation = CardDefaults.cardElevation(defaultElevation = 0.dp)
                        ) {
                            Column(
                                modifier = Modifier.padding(10.dp),
                                verticalArrangement = Arrangement.spacedBy(6.dp)
                            ) {
                                Text(
                                    text = game.name,
                                    fontWeight = FontWeight.Bold,
                                    maxLines = 1,
                                    overflow = TextOverflow.Ellipsis,
                                    fontSize = 14.sp
                                )
                                Text(
                                    text = "${game.saveUnits.count { it.unitType == com.eflay.gamesavemanager.model.SaveUnitType.Folder }} folder, " +
                                        "${game.saveUnits.count { it.unitType == com.eflay.gamesavemanager.model.SaveUnitType.File }} file",
                                    fontSize = 11.sp,
                                    color = MutedTextColor,
                                    maxLines = 1,
                                    overflow = TextOverflow.Ellipsis
                                )
                                Button(
                                    onClick = { onSelectGame(game) },
                                    modifier = Modifier.fillMaxWidth(),
                                    contentPadding = PaddingValues(horizontal = 8.dp, vertical = 4.dp)
                                ) {
                                    Text("Open", fontSize = 12.sp)
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
