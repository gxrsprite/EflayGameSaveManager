package com.eflay.gamesavemanager.ui.theme

import androidx.compose.material3.*
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color

val SurfaceColor = Color(0xFFF5F5F5)
val PanelColor = Color.White
val MutedTextColor = Color(0xFF888888)
val AccentColor = Color(0xFF4A90D9)
val FavoriteColor = Color(0xFFE8F0E4)
val DetailEditorBg = Color(0xFFF7FBF4)
val CloudDetailBg = Color(0xFFEDF4F7)
val GameDetailBg = Color(0xFFFFF8EE)

private val LightColorScheme = lightColorScheme(
    primary = Color(0xFF4A90D9),
    onPrimary = Color.White,
    surface = SurfaceColor,
    background = SurfaceColor,
    onBackground = Color(0xFF1C1B1F),
    onSurface = Color(0xFF1C1B1F),
    secondary = Color(0xFF625B71),
    tertiary = Color(0xFF7D5260)
)

@Composable
fun EflayTheme(content: @Composable () -> Unit) {
    MaterialTheme(
        colorScheme = LightColorScheme,
        content = content
    )
}
