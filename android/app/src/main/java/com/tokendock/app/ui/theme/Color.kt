package com.tokendock.app.ui.theme

import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.ui.graphics.Color

// —— TokenDock 蓝青色系（默认配色；动态颜色开启时在 Android 12+ 上覆盖） ——
val TealPrimaryLight = Color(0xFF0E7490)
val TealOnPrimaryLight = Color(0xFFFFFFFF)
val TealContainerLight = Color(0xFFCFFAFE)
val TealOnContainerLight = Color(0xFF083344)
val TealPrimaryDark = Color(0xFF22D3EE)
val TealOnPrimaryDark = Color(0xFF06222B)
val TealContainerDark = Color(0xFF155E6B)
val TealOnContainerDark = Color(0xFFCFFAFE)

val SurfaceLight = Color(0xFFF6F8FA)
val OnSurfaceLight = Color(0xFF111827)
val SurfaceVariantLight = Color(0xFFFFFFFF)
val OnSurfaceVariantLight = Color(0xFF4B5563)
val OutlineLight = Color(0xFFE2E8F0)

val SurfaceDark = Color(0xFF0F1720)
val OnSurfaceDark = Color(0xFFF3F4F6)
val SurfaceVariantDark = Color(0xFF1B2733)
val OnSurfaceVariantDark = Color(0xFF9CA3AF)
val OutlineDark = Color(0xFF2A3947)

val SuccessGreen = Color(0xFF16A34A)
val WarnAmber = Color(0xFFD97706)
val ErrorRed = Color(0xFFDC2626)
val NeutralGray = Color(0xFF9CA3AF)

internal val TokenDockLightColors = lightColorScheme(
    primary = TealPrimaryLight,
    onPrimary = TealOnPrimaryLight,
    primaryContainer = TealContainerLight,
    onPrimaryContainer = TealOnContainerLight,
    background = SurfaceLight,
    onBackground = OnSurfaceLight,
    surface = SurfaceVariantLight,
    onSurface = OnSurfaceLight,
    surfaceVariant = Color(0xFFF1F5F9),
    onSurfaceVariant = OnSurfaceVariantLight,
    outline = OutlineLight,
    error = ErrorRed,
)

internal val TokenDockDarkColors = darkColorScheme(
    primary = TealPrimaryDark,
    onPrimary = TealOnPrimaryDark,
    primaryContainer = TealContainerDark,
    onPrimaryContainer = TealOnContainerDark,
    background = SurfaceDark,
    onBackground = OnSurfaceDark,
    surface = SurfaceVariantDark,
    onSurface = OnSurfaceDark,
    surfaceVariant = Color(0xFF22303D),
    onSurfaceVariant = OnSurfaceVariantDark,
    outline = OutlineDark,
    error = Color(0xFFF87171),
)
