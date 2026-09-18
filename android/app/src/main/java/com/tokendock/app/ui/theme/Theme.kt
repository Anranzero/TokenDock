package com.tokendock.app.ui.theme

import android.os.Build
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.dynamicDarkColorScheme
import androidx.compose.material3.dynamicLightColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import com.tokendock.app.data.ThemeMode
import com.tokendock.app.ui.ios.ProvideIosTheme

/**
 * 应用主题：浅色 / 暗色 / 跟随系统；Android 12+ 可选动态颜色，默认使用 TokenDock 蓝青色。
 * Material 方案与 iOS 组件套件（IosKit）使用同一套 iOS 系统色板，保证弹窗、滑块等控件观感一致。
 */
@Composable
fun TokenDockTheme(
    themeMode: ThemeMode,
    dynamicColor: Boolean,
    content: @Composable () -> Unit,
) {
    val dark = when (themeMode) {
        ThemeMode.Light -> false
        ThemeMode.Dark -> true
        ThemeMode.System -> isSystemInDarkTheme()
    }

    val context = LocalContext.current
    val colorScheme = when {
        dynamicColor && Build.VERSION.SDK_INT >= Build.VERSION_CODES.S ->
            if (dark) dynamicDarkColorScheme(context) else dynamicLightColorScheme(context)

        dark -> DarkScheme
        else -> LightScheme
    }

    ProvideIosTheme(dark = dark) {
        MaterialTheme(colorScheme = colorScheme, content = content)
    }
}

// HyperOS 语义色 + TokenDock 蓝青主色
private val AccentLight = Color(0xFF0E7490)
private val AccentDark = Color(0xFF22D3EE)

private val LightScheme = lightColorScheme(
    primary = AccentLight,
    onPrimary = Color.White,
    background = Color(0xFFF3F5F8),
    onBackground = Color(0xFF14171C),
    surface = Color.White,
    onSurface = Color(0xFF14171C),
    surfaceVariant = Color(0xFFF3F5F8),
    onSurfaceVariant = Color(0x99272D38),
    outline = Color(0x1F0F172A),
    error = Color(0xFFDC2626),
)

private val DarkScheme = darkColorScheme(
    primary = AccentDark,
    onPrimary = Color(0xFF06222B),
    background = Color(0xFF17181B),
    onBackground = Color(0xFFECEEF2),
    surface = Color(0xFF212327),
    onSurface = Color(0xFFECEEF2),
    surfaceVariant = Color(0xFF2A2D32),
    onSurfaceVariant = Color(0x99A8B0BC),
    outline = Color(0x1AFFFFFF),
    error = Color(0xFFF87171),
)
