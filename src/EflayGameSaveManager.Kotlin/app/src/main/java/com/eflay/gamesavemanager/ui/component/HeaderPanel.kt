package com.eflay.gamesavemanager.ui.component

import androidx.compose.foundation.layout.*
import androidx.compose.material3.*
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.eflay.gamesavemanager.ui.theme.*

@Composable
fun HeaderPanel(
    currentDeviceSummary: String,
    configPathSummary: String,
    statusMessage: String,
    shizukuStatus: String,
    onReload: () -> Unit,
    onToggleAddGame: () -> Unit
) {
    Card(
        modifier = Modifier.fillMaxWidth(),
        colors = CardDefaults.cardColors(containerColor = PanelColor),
        elevation = CardDefaults.cardElevation(defaultElevation = 0.dp)
    ) {
        Column(
            modifier = Modifier.padding(14.dp),
            verticalArrangement = Arrangement.spacedBy(6.dp)
        ) {
            Text(
                text = "Android-first save sync",
                fontSize = 24.sp,
                fontWeight = FontWeight.Bold
            )
            Text(
                text = currentDeviceSummary,
                fontSize = 13.sp,
                color = MutedTextColor
            )
            Text(
                text = configPathSummary,
                fontSize = 12.sp,
                color = MutedTextColor
            )
            Text(
                text = statusMessage,
                fontSize = 13.sp,
                color = AccentColor
            )
            Text(
                text = shizukuStatus,
                fontSize = 12.sp,
                color = if (shizukuStatus.contains("ready")) androidx.compose.ui.graphics.Color(0xFF228B22)
                    else if (shizukuStatus.contains("permission")) androidx.compose.ui.graphics.Color(0xFFFF8C00)
                    else MutedTextColor
            )
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.spacedBy(10.dp)
            ) {
                Button(
                    onClick = onReload,
                    modifier = Modifier.weight(1f)
                ) {
                    Text("Reload")
                }
                Button(
                    onClick = onToggleAddGame,
                    modifier = Modifier.weight(1f)
                ) {
                    Text("Add game")
                }
            }
        }
    }
}
