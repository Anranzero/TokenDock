package com.tokendock.app.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.border

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Info
import androidx.compose.material.icons.filled.Lock
import androidx.compose.material.icons.filled.Refresh
import androidx.compose.material.icons.filled.Settings
import androidx.compose.material3.Button
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.tokendock.app.data.DisplayFormat
import com.tokendock.app.data.FetchFailureKind
import com.tokendock.app.data.QuotaState
import com.tokendock.app.data.UsageWindow
import com.tokendock.app.ui.theme.WarnAmber

/**
 * 首页：紧凑顶栏（应用名 / 状态 / 更新时间 / 操作）+ 三张额度卡 + 一行式 Token 说明。
 * 设计取向：iOS/macOS 简约——大号数字做主体、细胶囊进度条、次要信息弱化；
 * 无数据时只展示一张引导卡，不用三张「未知」占位。
 */
@Composable
fun HomeScreen(
    state: QuotaState,
    glassEnabled: Boolean,
    onOpenSettings: () -> Unit,
    onRefresh: () -> Unit,
    modifier: Modifier = Modifier,
) {
    val cardColor = if (glassEnabled) {
        MaterialTheme.colorScheme.surface.copy(alpha = 0.72f)
    } else {
        MaterialTheme.colorScheme.surface
    }

    Column(
        modifier = modifier
            .fillMaxWidth()
            .verticalScroll(rememberScrollState())
            .statusBarsPadding()
            .navigationBarsPadding()
            .padding(horizontal = 20.dp),
        verticalArrangement = Arrangement.spacedBy(14.dp),
    ) {
        Spacer(Modifier.height(6.dp))
        Header(state, onOpenSettings, onRefresh)

        if (state.lastGood == null) {
            EmptyStateCard(state, cardColor, onOpenSettings)
        } else {
            val data = state.lastGood
            QuotaCard("5 小时", "滚动窗口", data.rolling, state, cardColor)
            QuotaCard("本周", "每周窗口", data.weekly, state, cardColor)
            QuotaCard("本月", "月度窗口", data.monthly, state, cardColor)
        }

        TokenNote(cardColor)
        Spacer(Modifier.height(10.dp))
    }
}

// ---------- 顶栏 ----------

@Composable
private fun Header(state: QuotaState, onOpenSettings: () -> Unit, onRefresh: () -> Unit) {
    Row(verticalAlignment = Alignment.CenterVertically) {
        Column(modifier = Modifier.weight(1f)) {
            Text(
                text = "TokenDock",
                style = MaterialTheme.typography.headlineSmall,
                fontWeight = FontWeight.SemiBold,
            )
            Spacer(Modifier.height(6.dp))
            Row(verticalAlignment = Alignment.CenterVertically) {
                Box(
                    modifier = Modifier
                        .size(7.dp)
                        .background(statusColor(state), CircleShape),
                )
                Spacer(Modifier.width(6.dp))
                Text(
                    text = statusLine(state),
                    style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
        }
        IconButton(onClick = onRefresh) {
            Icon(
                Icons.Filled.Refresh,
                contentDescription = "立即刷新",
                tint = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }
        IconButton(onClick = onOpenSettings) {
            Icon(
                Icons.Filled.Settings,
                contentDescription = "设置",
                tint = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }
    }
}

// ---------- 空状态：一张引导卡，不摆三张「未知」 ----------

@Composable
private fun EmptyStateCard(state: QuotaState, containerColor: Color, onOpenSettings: () -> Unit) {
    val needsKey = state.failure == FetchFailureKind.NotConfigured
    Card2(containerColor) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Box(
                modifier = Modifier
                    .size(36.dp)
                    .background(MaterialTheme.colorScheme.primary.copy(alpha = 0.10f), RoundedCornerShape(12.dp)),
                contentAlignment = Alignment.Center,
            ) {
                Icon(
                    Icons.Filled.Lock,
                    contentDescription = null,
                    tint = MaterialTheme.colorScheme.primary,
                    modifier = Modifier.size(18.dp),
                )
            }
            Spacer(Modifier.width(12.dp))
            Column(Modifier.weight(1f)) {
                Text(
                    text = if (needsKey) "尚未设置 API Key" else "尚未获取数据",
                    style = MaterialTheme.typography.titleMedium,
                    fontWeight = FontWeight.Medium,
                )
                Spacer(Modifier.height(4.dp))
                Text(
                    text = if (needsKey) {
                        "填入 OpenCode 控制台的 API Key 后即可查看余量，密钥由系统 Keystore 加密保存在本机。"
                    } else {
                        state.statusText
                    },
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
        }
        Spacer(Modifier.height(14.dp))
        Button(onClick = onOpenSettings, modifier = Modifier.fillMaxWidth()) {
            Text(if (needsKey) "去设置" else "打开设置")
        }
    }
}

// ---------- 额度卡：标签 → 大号百分比 → 胶囊进度条 → 次要信息 ----------

@Composable
private fun QuotaCard(
    title: String,
    subtitle: String,
    window: UsageWindow?,
    state: QuotaState,
    containerColor: Color,
) {
    val remaining = window?.remainingPercent
    val valueColor = valueColor(remaining, state)

    Card2(containerColor) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Text(
                text = title,
                style = MaterialTheme.typography.titleMedium,
                fontWeight = FontWeight.Medium,
            )
            Spacer(Modifier.weight(1f))
            // 过期状态由顶栏统一提示，卡片内只保留窗口说明，避免三处重复橙色标记
            Text(
                text = subtitle,
                style = MaterialTheme.typography.labelSmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }

        Spacer(Modifier.height(10.dp))

        // 大号剩余百分比（数字为主角，% 小一号同基线）
        Row(verticalAlignment = Alignment.Bottom) {
            Text(
                text = remaining?.let { DisplayFormat.formatRemainingPercent(it).removeSuffix("%") } ?: "—",
                fontSize = 40.sp,
                fontWeight = FontWeight.Bold,
                color = valueColor,
                style = MaterialTheme.typography.displaySmall,
            )
            if (remaining != null) {
                Text(
                    text = "%",
                    style = MaterialTheme.typography.titleMedium,
                    color = valueColor,
                    modifier = Modifier.padding(start = 2.dp, bottom = 6.dp),
                )
            }
        }

        Spacer(Modifier.height(12.dp))

        // 胶囊进度条：未知时不画占位灰条
        if (remaining != null) {
            CapsuleProgress(
                progress = (remaining / 100.0).toFloat().coerceIn(0f, 1f),
                color = valueColor,
                track = MaterialTheme.colorScheme.onSurface.copy(alpha = 0.06f),
            )
            Spacer(Modifier.height(12.dp))
        }

        Text(
            text = caption(window, remaining),
            style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
    }
}

@Composable
private fun CapsuleProgress(progress: Float, color: Color, track: Color) {
    Box(
        modifier = Modifier
            .fillMaxWidth()
            .height(8.dp)
            .background(track, RoundedCornerShape(4.dp)),
    ) {
        if (progress > 0f) {
            Box(
                modifier = Modifier
                    .fillMaxWidth(progress)
                    .height(8.dp)
                    .background(color, RoundedCornerShape(4.dp)),
            )
        }
    }
}

// ---------- 一行式 Token 说明 ----------

@Composable
private fun TokenNote(containerColor: Color) {
    Card2(containerColor) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Icon(
                Icons.Filled.Info,
                contentDescription = null,
                tint = MaterialTheme.colorScheme.onSurfaceVariant,
                modifier = Modifier.size(16.dp),
            )
            Spacer(Modifier.width(8.dp))
            Text(
                text = "Token 明细：官方暂未提供",
                style = MaterialTheme.typography.bodyMedium,
                fontWeight = FontWeight.Medium,
            )
        }
        Spacer(Modifier.height(6.dp))
        Text(
            text = "官方额度接口只返回百分比；Android 端不读取电脑端数据，也不按百分比反推 Token。",
            style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
    }
}

// ---------- 通用卡片容器：浅描边 + 大圆角，不用阴影 ----------

@Composable
private fun Card2(containerColor: Color, content: @Composable () -> Unit) {
    Surface(
        modifier = Modifier
            .fillMaxWidth()
            .border(
                width = 1.dp,
                color = MaterialTheme.colorScheme.outline.copy(alpha = 0.5f),
                shape = RoundedCornerShape(20.dp),
            ),
        shape = RoundedCornerShape(20.dp),
        color = containerColor,
    ) {
        Column(modifier = Modifier.padding(horizontal = 18.dp, vertical = 16.dp)) {
            content()
        }
    }
}

// ---------- 文案与配色 ----------

private fun statusLine(state: QuotaState): String {
    val data = state.lastGood
    return when {
        state.refreshing && data == null -> "刷新中…"
        data == null -> if (state.failure == FetchFailureKind.NotConfigured) "未设置密钥" else "尚未获取"
        state.isStale -> "数据已过期 · 最后更新 ${DisplayFormat.formatClock(data.fetchedAtMillis)}"
        else -> "更新于 ${DisplayFormat.formatClock(data.fetchedAtMillis)}"
    }
}

private fun statusColor(state: QuotaState): Color {
    val data = state.lastGood
    return when {
        state.refreshing -> NeutralGray
        data == null -> if (state.failure == FetchFailureKind.NotConfigured) NeutralGray else ErrorRed
        state.isStale -> WarnAmber
        else -> SuccessGreen
    }
}

/** 百分比配色：正常=主题色，≤20%=琥珀，数据过期=灰，未知=次要灰。 */
private fun valueColor(remaining: Double?, state: QuotaState): Color = when {
    state.isStale -> NeutralGray
    remaining == null -> NeutralGray
    remaining <= 20.0 -> WarnAmber
    else -> TealAccent
}

private fun caption(window: UsageWindow?, remaining: Double?): String = when {
    window == null -> "接口未返回该窗口"
    remaining == null -> "接口未提供百分比 · ${DisplayFormat.formatCountdown(window.resetsAtMillis)}"
    else -> "剩余 ${DisplayFormat.formatRemainingPercent(remaining)} · ${
        DisplayFormat.formatStatus(window.status)
    } · ${DisplayFormat.formatCountdown(window.resetsAtMillis)}"
}

private val TealAccent = Color(0xFF0E7490)
private val SuccessGreen = Color(0xFF16A34A)
private val NeutralGray = Color(0xFF9CA3AF)
private val ErrorRed = Color(0xFFDC2626)
