package com.tokendock.app.ui

import android.Manifest
import android.app.Activity
import android.content.Context
import android.os.Build
import androidx.activity.compose.BackHandler
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Lock
import androidx.compose.material.icons.outlined.Notifications
import androidx.compose.material.icons.outlined.Refresh
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Slider
import androidx.compose.material3.SliderDefaults
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.core.app.ActivityCompat
import com.tokendock.app.data.FetchFailureKind
import com.tokendock.app.data.FetchResult
import com.tokendock.app.data.SecureKeyStore
import com.tokendock.app.data.SettingsStore
import com.tokendock.app.data.ThemeMode
import com.tokendock.app.data.UsageApiClient
import com.tokendock.app.ui.ios.IosColors
import com.tokendock.app.ui.ios.IosFooter
import com.tokendock.app.ui.ios.IosRow
import com.tokendock.app.ui.ios.IosRowBlock
import com.tokendock.app.ui.ios.IosSection
import com.tokendock.app.ui.ios.IosSectionHeader
import com.tokendock.app.ui.ios.IosSegmentedControl
import com.tokendock.app.ui.ios.IosSeparator
import com.tokendock.app.ui.ios.IosSwitch
import com.tokendock.app.ui.ios.iosColors
import com.tokendock.app.work.LowQuotaNotifier
import kotlinx.coroutines.launch

/**
 * 设置页（iOS 分组列表版式）：账户 / 刷新与提醒 / 外观 / 数据来源 四组，
 * 行内使用 iOS 开关与分段控件；API Key 通过弹窗输入并「验证后保存」。
 */
@Composable
fun SettingsScreen(
    onThemeChanged: () -> Unit,
    onBack: () -> Unit,
    modifier: Modifier = Modifier,
) {
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    val colors = iosColors()

    var hasKey by remember { mutableStateOf(SecureKeyStore.hasKey(context)) }
    var showKeyDialog by remember { mutableStateOf(false) }
    var showClearDialog by remember { mutableStateOf(false) }

    var refreshSeconds by remember { mutableStateOf(SettingsStore.refreshSeconds(context)) }
    var threshold by remember { mutableStateOf(SettingsStore.lowThreshold(context).toFloat()) }
    var themeMode by remember { mutableStateOf(SettingsStore.themeMode(context)) }
    var dynamicColor by remember { mutableStateOf(SettingsStore.dynamicColorEnabled(context)) }
    var glass by remember { mutableStateOf(SettingsStore.glassPreference(context)) }
    var notifications by remember { mutableStateOf(SettingsStore.notificationsEnabled(context)) }
    var refreshMenuOpen by remember { mutableStateOf(false) }

    val glassSupported = SettingsStore.isGlassSupported(context)
    val dynamicSupported = Build.VERSION.SDK_INT >= Build.VERSION_CODES.S

    BackHandler { onBack() }

    Column(
        modifier = modifier
            .fillMaxWidth()
            .verticalScroll(rememberScrollState())
            .statusBarsPadding()
            .navigationBarsPadding(),
    ) {
        // iOS 返回：左上角文字按钮
        Text(
            text = "‹ 返回",
            fontSize = 17.sp,
            color = colors.accent,
            modifier = Modifier
                .padding(start = 16.dp, top = 12.dp)
                .clickable { onBack() }
                .padding(4.dp),
        )

        Text(
            "设置",
            fontSize = 34.sp,
            fontWeight = FontWeight.Bold,
            color = colors.label,
            modifier = Modifier.padding(start = 20.dp, top = 8.dp, bottom = 4.dp),
        )

        // ---- 账户 ----
        IosSectionHeader("账户")
        IosSection {
            IosRow(
                title = "API Key",
                subtitle = if (hasKey) "已保存 · 点击更换" else "尚未设置",
                icon = Icons.Outlined.Lock,
                iconTint = colors.accent,
                showChevron = true,
                onClick = { showKeyDialog = true },
            )
            if (hasKey) {
                IosSeparator()
                IosRow(
                    title = "清除密钥",
                    onClick = { showClearDialog = true },
                    modifier = Modifier,
                )
            }
        }
        IosFooter("密钥使用 Android Keystore（AES-256/GCM）加密，仅本机可解密；不写入日志、不上传第三方。")

        // ---- 刷新与提醒 ----
        IosSectionHeader("刷新与提醒")
        IosSection {
            Box {
                IosRow(
                    title = "前台刷新间隔",
                    subtitle = "界面打开时自动刷新",
                    icon = Icons.Outlined.Refresh,
                    iconTint = colors.accent,
                    trailingText = "$refreshSeconds 秒",
                    showChevron = true,
                    onClick = { refreshMenuOpen = true },
                )
                DropdownMenu(expanded = refreshMenuOpen, onDismissRequest = { refreshMenuOpen = false }) {
                    listOf(30, 60, 120, 300).forEach { seconds ->
                        DropdownMenuItem(
                            text = { Text("$seconds 秒") },
                            onClick = {
                                refreshMenuOpen = false
                                refreshSeconds = seconds
                                SettingsStore.setRefreshSeconds(context, seconds)
                            },
                        )
                    }
                }
            }
            IosSeparator()
            IosRow(title = "低余量提醒阈值", trailingText = "${threshold.toInt()}%")
            IosRowBlock {
                Slider(
                    value = threshold,
                    onValueChange = { threshold = it },
                    onValueChangeFinished = {
                        SettingsStore.setLowThreshold(context, threshold.toInt())
                        LowQuotaNotifier.resetDedup(context)
                    },
                    valueRange = 5f..50f,
                    steps = 8,
                    colors = SliderDefaults.colors(
                        thumbColor = Color.White,
                        activeTrackColor = colors.accent,
                        inactiveTrackColor = colors.fill,
                    ),
                )
            }
            IosSeparator()
            IosRow(
                title = "通知",
                icon = Icons.Outlined.Notifications,
                iconTint = colors.accent,
                trailing = {
                    IosSwitch(
                        checked = notifications,
                        onCheckedChange = { enabled ->
                            notifications = enabled
                            SettingsStore.setNotificationsEnabled(context, enabled)
                            if (enabled) {
                                LowQuotaNotifier.ensureChannel(context)
                                requestNotificationPermissionIfNeeded(context)
                            }
                        },
                    )
                },
            )
        }
        IosFooter("后台刷新由系统调度（最短 15 分钟）；剩余低于阈值时提醒一次，同一重置周期不重复。")

        // ---- 外观 ----
        IosSectionHeader("外观")
        IosSection {
            IosRow(title = "主题")
            IosRowBlock {
                IosSegmentedControl(
                    options = listOf("跟随系统", "浅色", "暗色"),
                    selectedIndex = when (themeMode) {
                        ThemeMode.System -> 0
                        ThemeMode.Light -> 1
                        ThemeMode.Dark -> 2
                    },
                    onSelected = { index ->
                        themeMode = when (index) {
                            1 -> ThemeMode.Light
                            2 -> ThemeMode.Dark
                            else -> ThemeMode.System
                        }
                        SettingsStore.setThemeMode(context, themeMode)
                        onThemeChanged()
                    },
                )
            }
            IosSeparator()
            IosRow(
                title = "动态颜色",
                subtitle = if (dynamicSupported) "使用系统壁纸取色（默认关闭）" else "需要 Android 12 及以上",
                trailing = {
                    IosSwitch(
                        checked = dynamicColor && dynamicSupported,
                        enabled = dynamicSupported,
                        onCheckedChange = { enabled ->
                            dynamicColor = enabled
                            SettingsStore.setDynamicColorEnabled(context, enabled)
                            onThemeChanged()
                        },
                    )
                },
            )
            IosSeparator()
            IosRow(
                title = "毛玻璃效果",
                subtitle = if (glassSupported) "半透明卡片与窗口模糊" else "当前设备不支持，已自动关闭",
                trailing = {
                    IosSwitch(
                        checked = glass && glassSupported,
                        enabled = glassSupported,
                        onCheckedChange = { enabled ->
                            glass = enabled
                            SettingsStore.setGlassEnabled(context, enabled)
                            onThemeChanged()
                        },
                    )
                },
            )
        }
        IosFooter("默认使用 TokenDock 蓝青色；毛玻璃在 Android 12+ 且非低内存设备上生效。")

        // ---- 数据来源 ----
        IosSectionHeader("数据来源")
        IosFooter(
            "额度数据仅来自官方接口 GET https://opencode.ai/zen/go/v1/usage，使用你的 API Key 鉴权，账号级数据与官网一致。\n\n" +
                "本应用不读取、不上传任何本机其他数据；Token 明细官方未提供，因此不做展示，也不按剩余百分比反推。",
        )

        Spacer(Modifier.height(40.dp))
    }

    if (showKeyDialog) {
        ApiKeyDialog(
            colors = colors,
            onDismiss = { showKeyDialog = false },
            onSaved = {
                hasKey = SecureKeyStore.hasKey(context)
                showKeyDialog = false
            },
        )
    }

    if (showClearDialog) {
        AlertDialog(
            onDismissRequest = { showClearDialog = false },
            title = { Text("清除密钥？", fontSize = 17.sp, fontWeight = FontWeight.SemiBold) },
            text = { Text("清除后需要重新填写 API Key 才能获取额度。", fontSize = 15.sp) },
            confirmButton = {
                TextButton(onClick = {
                    SecureKeyStore.clear(context)
                    hasKey = false
                    showClearDialog = false
                }) { Text("清除", color = colors.red) }
            },
            dismissButton = {
                TextButton(onClick = { showClearDialog = false }) { Text("取消", color = colors.accent) }
            },
        )
    }
}

/** API Key 弹窗：输入 → 调官方接口验证 → 通过才写入 Keystore。 */
@Composable
private fun ApiKeyDialog(colors: IosColors, onDismiss: () -> Unit, onSaved: () -> Unit) {
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    var keyInput by remember { mutableStateOf("") }
    var message by remember { mutableStateOf<String?>(null) }
    var validating by remember { mutableStateOf(false) }

    AlertDialog(
        onDismissRequest = { if (!validating) onDismiss() },
        title = { Text("API Key", fontSize = 17.sp, fontWeight = FontWeight.SemiBold) },
        text = {
            Column {
                Text(
                    "粘贴 OpenCode 控制台生成的 API Key，验证通过后加密保存在本机。",
                    fontSize = 13.sp,
                    color = colors.secondaryLabel,
                )
                Spacer(Modifier.height(12.dp))
                OutlinedTextField(
                    value = keyInput,
                    onValueChange = { keyInput = it },
                    modifier = Modifier.fillMaxWidth(),
                    singleLine = true,
                    shape = RoundedCornerShape(10.dp),
                    enabled = !validating,
                    placeholder = { Text("sk-…", color = colors.tertiaryLabel) },
                    visualTransformation = PasswordVisualTransformation(),
                )
                message?.let {
                    Spacer(Modifier.height(10.dp))
                    Text(it, fontSize = 13.sp, color = colors.red)
                }
            }
        },
        confirmButton = {
            TextButton(
                enabled = !validating,
                onClick = {
                    val key = keyInput.trim()
                    if (key.isEmpty()) {
                        message = "请先粘贴 API Key"
                        return@TextButton
                    }
                    validating = true
                    message = null
                    scope.launch {
                        val result = UsageApiClient().fetch(key)
                        validating = false
                        val accepted = result is FetchResult.Ok ||
                            (result as? FetchResult.Fail)?.kind == FetchFailureKind.ParseError
                        if (accepted) {
                            SecureKeyStore.save(context, key)
                            onSaved()
                        } else {
                            message = (result as? FetchResult.Fail)?.message ?: "验证失败，请重试"
                        }
                    }
                },
            ) { Text(if (validating) "验证中…" else "验证并保存", color = colors.accent) }
        },
        dismissButton = {
            TextButton(enabled = !validating, onClick = onDismiss) { Text("取消", color = colors.accent) }
        },
    )
}

/** Android 13+ 开启通知时请求运行时权限（未授权则静默跳过，不影响其他功能）。 */
private fun requestNotificationPermissionIfNeeded(context: Context) {
    if (Build.VERSION.SDK_INT < Build.VERSION_CODES.TIRAMISU) return
    val activity = context as? Activity ?: return
    ActivityCompat.requestPermissions(activity, arrayOf(Manifest.permission.POST_NOTIFICATIONS), 1001)
}
