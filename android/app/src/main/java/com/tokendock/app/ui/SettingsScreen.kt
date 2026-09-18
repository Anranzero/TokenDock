package com.tokendock.app.ui

import android.Manifest
import android.app.Activity
import android.content.Context
import android.os.Build
import androidx.activity.compose.BackHandler
import androidx.compose.foundation.layout.Arrangement
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
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.FilterChip
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Slider
import androidx.compose.material3.Switch
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
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.unit.dp
import androidx.core.app.ActivityCompat
import com.tokendock.app.data.FetchResult
import com.tokendock.app.data.SecureKeyStore
import com.tokendock.app.data.SettingsStore
import com.tokendock.app.data.ThemeMode
import com.tokendock.app.data.UsageApiClient
import com.tokendock.app.work.LowQuotaNotifier
import com.tokendock.app.work.UsageRefreshWorker
import kotlinx.coroutines.launch

/**
 * 设置页：API Key（Keystore 加密保存，先验证后落盘）、刷新间隔、低余量阈值、
 * 主题、动态颜色、毛玻璃、通知开关、数据来源说明。
 */
@Composable
fun SettingsScreen(
    onThemeChanged: () -> Unit,
    onBack: () -> Unit,
    modifier: Modifier = Modifier,
) {
    val context = LocalContext.current
    val scope = rememberCoroutineScope()

    var keyInput by remember { mutableStateOf("") }
    var keyMessage by remember { mutableStateOf<String?>(null) }
    var validating by remember { mutableStateOf(false) }

    var refreshSeconds by remember { mutableStateOf(SettingsStore.refreshSeconds(context)) }
    var threshold by remember { mutableStateOf(SettingsStore.lowThreshold(context).toFloat()) }
    var themeMode by remember { mutableStateOf(SettingsStore.themeMode(context)) }
    var dynamicColor by remember { mutableStateOf(SettingsStore.dynamicColorEnabled(context)) }
    var glass by remember { mutableStateOf(SettingsStore.glassPreference(context)) }
    var notifications by remember { mutableStateOf(SettingsStore.notificationsEnabled(context)) }
    var refreshMenuOpen by remember { mutableStateOf(false) }

    val glassSupported = SettingsStore.isGlassSupported(context)

    BackHandler { onBack() }

    Column(
        modifier = modifier
            .fillMaxWidth()
            .verticalScroll(rememberScrollState())
            .statusBarsPadding()
            .navigationBarsPadding()
            .padding(horizontal = 16.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        Spacer(Modifier.height(4.dp))
        Row(verticalAlignment = Alignment.CenterVertically) {
            IconButton(onClick = onBack) {
                Icon(Icons.AutoMirrored.Filled.ArrowBack, contentDescription = "返回")
            }
            Text("设置", style = MaterialTheme.typography.titleLarge, fontWeight = FontWeight.SemiBold)
        }

        // ---- API Key ----
        SettingsCard("API Key") {
            Text(
                "OpenCode 控制台生成的 API Key；由 Android Keystore 加密保存（AES-256/GCM），" +
                    "仅本机可解密，不写入日志、不上传第三方。",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
            Spacer(Modifier.height(10.dp))
            OutlinedTextField(
                value = keyInput,
                onValueChange = { keyInput = it },
                modifier = Modifier.fillMaxWidth(),
                singleLine = true,
                label = { Text(if (SecureKeyStore.hasKey(context)) "已保存（可粘贴新密钥覆盖）" else "粘贴 API Key") },
                visualTransformation = PasswordVisualTransformation(),
            )
            Spacer(Modifier.height(10.dp))
            Row(verticalAlignment = Alignment.CenterVertically) {
                Button(
                    onClick = {
                        val key = keyInput.trim()
                        if (key.isEmpty()) {
                            keyMessage = "请先粘贴 API Key"
                            return@Button
                        }
                        validating = true
                        keyMessage = null
                        scope.launch {
                            val result = UsageApiClient().fetch(key)
                            validating = false
                            when (result) {
                                is FetchResult.Ok, is FetchResult.Fail -> {
                                    val accepted = result is FetchResult.Ok ||
                                        (result as? FetchResult.Fail)?.kind == com.tokendock.app.data.FetchFailureKind.ParseError
                                    if (accepted) {
                                        SecureKeyStore.save(context, key)
                                        keyInput = ""
                                        keyMessage = "密钥验证成功，已加密保存。"
                                    } else {
                                        keyMessage = (result as FetchResult.Fail).message
                                    }
                                }
                            }
                        }
                    },
                    enabled = !validating,
                ) { Text(if (validating) "验证中…" else "保存并验证") }
                Spacer(Modifier.padding(horizontal = 4.dp))
                OutlinedButton(
                    onClick = {
                        SecureKeyStore.clear(context)
                        keyMessage = "已清除本机保存的密钥。"
                    },
                ) { Text("清除") }
            }
            keyMessage?.let {
                Spacer(Modifier.height(8.dp))
                Text(it, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.primary)
            }
        }

        // ---- 刷新与提醒 ----
        SettingsCard("刷新与提醒") {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Column(Modifier.weight(1f)) {
                    Text("前台刷新间隔", style = MaterialTheme.typography.bodyMedium)
                    Text(
                        "后台刷新由系统调度（最小 15 分钟）；前台每 $refreshSeconds 秒自动刷新",
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                }
                Box {
                    TextButton(onClick = { refreshMenuOpen = true }) { Text("$refreshSeconds 秒") }
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
            }
            Spacer(Modifier.height(8.dp))
            Text(
                "低余量提醒阈值：${threshold.toInt()}%",
                style = MaterialTheme.typography.bodyMedium,
            )
            Slider(
                value = threshold,
                onValueChange = { threshold = it },
                onValueChangeFinished = {
                    SettingsStore.setLowThreshold(context, threshold.toInt())
                    LowQuotaNotifier.resetDedup(context)
                },
                valueRange = 5f..50f,
                steps = 8,
            )
            Row(verticalAlignment = Alignment.CenterVertically) {
                Column(Modifier.weight(1f)) {
                    Text("通知", style = MaterialTheme.typography.bodyMedium)
                    Text(
                        "任一窗口剩余低于阈值时提醒一次（同一重置周期不重复）",
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                }
                Switch(
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
            }
        }

        // ---- 外观 ----
        SettingsCard("外观") {
            Text("主题", style = MaterialTheme.typography.bodyMedium)
            Spacer(Modifier.height(8.dp))
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                ThemeChip("跟随系统", themeMode == ThemeMode.System) {
                    themeMode = ThemeMode.System
                    SettingsStore.setThemeMode(context, ThemeMode.System)
                    onThemeChanged()
                }
                ThemeChip("浅色", themeMode == ThemeMode.Light) {
                    themeMode = ThemeMode.Light
                    SettingsStore.setThemeMode(context, ThemeMode.Light)
                    onThemeChanged()
                }
                ThemeChip("暗色", themeMode == ThemeMode.Dark) {
                    themeMode = ThemeMode.Dark
                    SettingsStore.setThemeMode(context, ThemeMode.Dark)
                    onThemeChanged()
                }
            }
            Spacer(Modifier.height(12.dp))
            SwitchRow(
                title = "动态颜色",
                subtitle = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
                    "使用系统壁纸取色（默认关闭，关闭时使用 TokenDock 蓝青色）"
                } else {
                    "需要 Android 12 及以上"
                },
                checked = dynamicColor,
                enabled = Build.VERSION.SDK_INT >= Build.VERSION_CODES.S,
            ) { enabled ->
                dynamicColor = enabled
                SettingsStore.setDynamicColorEnabled(context, enabled)
                onThemeChanged()
            }
            Spacer(Modifier.height(8.dp))
            SwitchRow(
                title = "毛玻璃效果",
                subtitle = if (glassSupported) {
                    "卡片使用半透明与背景模糊（Android 12+，低内存设备自动关闭）"
                } else {
                    "当前设备不支持（需 Android 12+ 且非低内存设备），已自动使用不透明背景"
                },
                checked = glass && glassSupported,
                enabled = glassSupported,
            ) { enabled ->
                glass = enabled
                SettingsStore.setGlassEnabled(context, enabled)
                onThemeChanged()
            }
        }

        // ---- 数据来源 ----
        SettingsCard("数据来源说明") {
            Text(
                "额度数据仅来自官方接口 GET https://opencode.ai/zen/go/v1/usage，" +
                    "使用你填写的 API Key 鉴权，账号级数据与官网一致。\n\n" +
                    "本应用不读取、也不联网上传任何本机其他数据；" +
                    "Token 明细官方暂未提供，因此不做展示，也不会按剩余百分比反推 Token 数量。\n\n" +
                    "后续版本可能提供“连接 TokenDock 桌面端同步本机 Token 统计”的可选功能，" +
                    "当前版本不包含任何后端服务。",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }

        Spacer(Modifier.height(16.dp))
    }
}

@Composable
private fun SettingsCard(title: String, content: @Composable () -> Unit) {
    Card(
        modifier = Modifier.fillMaxWidth(),
        shape = RoundedCornerShape(18.dp),
        colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surface),
        elevation = CardDefaults.cardElevation(defaultElevation = 0.dp),
    ) {
        Column(Modifier.padding(16.dp)) {
            Text(title, style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.Medium)
            Spacer(Modifier.height(10.dp))
            content()
        }
    }
}

@Composable
private fun ThemeChip(label: String, selected: Boolean, onClick: () -> Unit) {
    FilterChip(selected = selected, onClick = onClick, label = { Text(label) })
}

@Composable
private fun SwitchRow(
    title: String,
    subtitle: String,
    checked: Boolean,
    enabled: Boolean = true,
    onCheckedChange: (Boolean) -> Unit,
) {
    Row(verticalAlignment = Alignment.CenterVertically) {
        Column(Modifier.weight(1f)) {
            Text(title, style = MaterialTheme.typography.bodyMedium)
            Text(
                subtitle,
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }
        Switch(checked = checked, enabled = enabled, onCheckedChange = onCheckedChange)
    }
}

/** Android 13+ 开启通知时请求运行时权限（未授权则通知静默跳过，不影响其他功能）。 */
private fun requestNotificationPermissionIfNeeded(context: Context) {
    if (Build.VERSION.SDK_INT < Build.VERSION_CODES.TIRAMISU) return
    val activity = context as? Activity ?: return
    if (ActivityCompat.shouldShowRequestPermissionRationale(activity, Manifest.permission.POST_NOTIFICATIONS)) return
    ActivityCompat.requestPermissions(activity, arrayOf(Manifest.permission.POST_NOTIFICATIONS), 1001)
}
