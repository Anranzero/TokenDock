package com.tokendock.app.ui.theme

import android.os.Build
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.dynamicDarkColorScheme
import androidx.compose.material3.dynamicLightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.platform.LocalContext
import com.tokendock.app.data.ThemeMode

/**
 * 应用主题：浅色 / 暗色 / 跟随系统；Android 12+ 可选动态颜色，
 * 默认始终使用 TokenDock 蓝青色系。
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

        dark -> TokenDockDarkColors
        else -> TokenDockLightColors
    }

    MaterialTheme(colorScheme = colorScheme, content = content)
}
