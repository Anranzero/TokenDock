package com.tokendock.app.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Refresh
import androidx.compose.material.icons.filled.Settings
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.tokendock.app.data.DisplayFormat
import com.tokendock.app.data.FetchFailureKind
import com.tokendock.app.data.QuotaState
import com.tokendock.app.data.UsageWindow
import com.tokendock.app.ui.theme.ErrorRed
import com.tokendock.app.ui.theme.NeutralGray
import com.tokendock.app.ui.theme.SuccessGreen
import com.tokendock.app.ui.theme.WarnAmber

/**
 * 首页：顶部（TokenDock / 连接状态 / 更新时间 / 设置）+ 三张额度卡片 + Token 明细说明。
 * 所有尺寸使用 dp/sp 由系统字体缩放参与排版，不写死行高，适配大字体与大屏。
 */
@Composable
fun HomeScreen(
    state: QuotaState,
    glassEnabled: Boolean,
    onOpenSettings: () -> Unit,
    onRefresh: () -> Unit,
    modifier: Modifier = Modifier,
) {
    Column(
        modifier = modifier
            .fillMaxWidth()
            .verticalScroll(rememberScrollState())
            .padding(horizontal = 16.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        // 毛玻璃：卡片使用半透明容器（真实背景模糊由窗口层负责，设备不支持时自动关闭）
        val cardColor = if (glassEnabled) {
            MaterialTheme.colorScheme.surface.copy(alpha = 0.72f)
        } else {
            MaterialTheme.colorScheme.surface
        }

        Spacer(Modifier.height(4.dp))
        HomeHeader(state, onOpenSettings, onRefresh)
        QuotaCard("5 小时用量", "滚动窗口 · 与服务端周期对齐", state.lastGood?.rolling, state, cardColor)
        QuotaCard("本周用量", "每周窗口", state.lastGood?.weekly, state, cardColor)
        QuotaCard("本月用量", "月度窗口", state.lastGood?.monthly, state, cardColor)
        TokenDetailNote(cardColor)
        Spacer(Modifier.height(16.dp))
    }
}

@Composable
private fun HomeHeader(state: QuotaState, onOpenSettings: () -> Unit, onRefresh: () -> Unit) {
    Row(
        modifier = Modifier.fillMaxWidth(),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Column(modifier = Modifier.weight(1f)) {
            Text(
                text = "TokenDock",
                style = MaterialTheme.typography.titleLarge,
                fontWeight = FontWeight.SemiBold,
            )
            Spacer(Modifier.height(4.dp))
            Row(verticalAlignment = Alignment.CenterVertically) {
                Box(
                    modifier = Modifier
                        .size(8.dp)
                        .background(statusDotColor(state), CircleShape),
                )
                Spacer(Modifier.width(6.dp))
                Text(
                    text = shortStatus(state),
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
            Spacer(Modifier.height(2.dp))
            Text(
                text = updatedLine(state),
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }
        IconButton(onClick = onRefresh) {
            Icon(Icons.Filled.Refresh, contentDescription = "立即刷新")
        }
        IconButton(onClick = onOpenSettings) {
            Icon(Icons.Filled.Settings, contentDescription = "设置")
        }
    }
}

@Composable
private fun QuotaCard(
    title: String,
    subtitle: String,
    window: UsageWindow?,
    state: QuotaState,
    containerColor: Color,
) {
    Card(
        modifier = Modifier.fillMaxWidth(),
        shape = RoundedCornerShape(18.dp),
        colors = CardDefaults.cardColors(containerColor = containerColor),
        elevation = CardDefaults.cardElevation(defaultElevation = 0.dp),
    ) {
        Column(modifier = Modifier.padding(16.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Column(modifier = Modifier.weight(1f)) {
                    Text(title, style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.Medium)
                    Text(
                        subtitle,
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                }
                Text(
                    text = DisplayFormat.formatRemainingPercent(window?.remainingPercent),
                    style = MaterialTheme.typography.headlineMedium,
                    fontWeight = FontWeight.Bold,
                    color = if (state.isStale) {
                        MaterialTheme.colorScheme.onSurfaceVariant
                    } else {
                        levelColor(window?.remainingPercent)
                    },
                )
            }
            Spacer(Modifier.height(10.dp))
            val progress = (window?.remainingPercent?.div(100.0))?.toFloat()
            if (progress != null) {
                LinearProgressIndicator(
                    progress = { progress.coerceIn(0f, 1f) },
                    modifier = Modifier
                        .fillMaxWidth()
                        .height(6.dp),
                    color = if (state.isStale) MaterialTheme.colorScheme.outline else levelColor(window.remainingPercent),
                    trackColor = MaterialTheme.colorScheme.surfaceVariant,
                )
            } else {
                // 接口未返回百分比：不画进度条，也不假设为 0/100
                Box(
                    modifier = Modifier
                        .fillMaxWidth()
                        .height(6.dp)
                        .background(MaterialTheme.colorScheme.surfaceVariant, RoundedCornerShape(3.dp)),
                )
            }
            Spacer(Modifier.height(10.dp))
            Text(
                text = windowLine(window),
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }
    }
}

/** Token 明细说明：官方未提供账号级逐模型明细，Android 端也不读取电脑端数据库。 */
@Composable
private fun TokenDetailNote(containerColor: Color) {
    Card(
        modifier = Modifier.fillMaxWidth(),
        shape = RoundedCornerShape(18.dp),
        colors = CardDefaults.cardColors(containerColor = containerColor),
        elevation = CardDefaults.cardElevation(defaultElevation = 0.dp),
    ) {
        Column(modifier = Modifier.padding(16.dp)) {
            Text("Token 明细", style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.Medium)
            Spacer(Modifier.height(6.dp))
            Text(
                "官方暂未提供 Token 明细",
                style = MaterialTheme.typography.bodyMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
            Spacer(Modifier.height(6.dp))
            Text(
                "官方额度接口目前只返回百分比，没有账号级逐模型 Token 数据；" +
                    "Android 端也无法读取电脑上 OpenCode / Zcode 的本地数据库，" +
                    "因此不展示 Token 数，也不会根据余量百分比反推。",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }
    }
}

// —— 文案与配色（与桌面端一致的口径） ——

private fun shortStatus(state: QuotaState): String = when {
    state.refreshing -> "刷新中…"
    state.lastGood != null && !state.isStale -> "正常"
    state.isStale -> "数据已过期"
    state.failure == FetchFailureKind.NotConfigured -> "未设置密钥"
    state.failure != FetchFailureKind.None -> "获取失败"
    else -> "尚未获取"
}

/** 状态圆点：正常=绿，失败=红，其余（已过期/未设置/尚未获取）=灰。 */
private fun statusDotColor(state: QuotaState): Color = when {
    state.lastGood != null && !state.isStale -> SuccessGreen
    state.failure != FetchFailureKind.None && !state.isStale && state.lastGood == null -> ErrorRed
    else -> NeutralGray
}

private fun updatedLine(state: QuotaState): String {
    val data = state.lastGood
    return when {
        data == null && state.failure == FetchFailureKind.None -> "尚未获取数据"
        data == null -> state.statusText
        state.isStale -> "最后成功：${DisplayFormat.formatClock(data.fetchedAtMillis)}"
        else -> "更新于 ${DisplayFormat.formatClock(data.fetchedAtMillis)}"
    }
}

private fun windowLine(window: UsageWindow?): String {
    if (window == null) return "接口未返回该窗口"
    val status = DisplayFormat.formatStatus(window.status)
    val countdown = DisplayFormat.formatCountdown(window.resetsAtMillis)
    val remaining = window.remainingPercent
    val remainingText = if (remaining == null) "剩余未知" else "剩余 ${DisplayFormat.formatRemainingPercent(remaining)}"
    return "$status · $remainingText · $countdown"
}

private fun levelColor(remaining: Double?): Color = when {
    remaining == null -> WarnAmber
    remaining >= 20 -> SuccessGreen
    else -> WarnAmber
}
