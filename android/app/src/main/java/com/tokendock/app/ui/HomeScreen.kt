package com.tokendock.app.ui

import androidx.compose.animation.AnimatedVisibility
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
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
import androidx.compose.material.icons.outlined.ExpandLess
import androidx.compose.material.icons.outlined.ExpandMore
import androidx.compose.material.icons.outlined.Key
import androidx.compose.material.icons.outlined.Refresh
import androidx.compose.material.icons.outlined.Settings
import androidx.compose.material.icons.outlined.WarningAmber
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.Text
import androidx.compose.material3.pulltorefresh.PullToRefreshBox
import androidx.compose.material3.pulltorefresh.PullToRefreshDefaults.Indicator
import androidx.compose.material3.pulltorefresh.rememberPullToRefreshState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.tokendock.app.data.DisplayFormat
import com.tokendock.app.data.FetchFailureKind
import com.tokendock.app.data.QuotaState
import com.tokendock.app.data.UsageWindow
import com.tokendock.app.ui.ios.IosBar
import com.tokendock.app.ui.ios.IosCard
import com.tokendock.app.ui.ios.IosColors
import com.tokendock.app.ui.ios.IosSeparator
import com.tokendock.app.ui.ios.IosStatusPill
import com.tokendock.app.ui.ios.iosColors

/**
 * 首页（方案 A · 极简卡片版）：
 * 两层顶部（标题 + 状态行 / 右上角统一大小的刷新与设置）→ 轻量内联警告卡 →
 * 订阅摘要卡 → 三张余量卡（名称 / 大数字 / 6dp 条形进度 / 倒计时，点卡展开详情）→
 * 本机 Token 统计卡（功能卡 + 优雅空状态）。下拉刷新，无底部按钮。
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun HomeScreen(
    state: QuotaState,
    glassEnabled: Boolean,
    onOpenSettings: () -> Unit,
    onRefresh: () -> Unit,
    modifier: Modifier = Modifier,
) {
    val colors = iosColors()

    val pullState = rememberPullToRefreshState()

    PullToRefreshBox(
        isRefreshing = state.refreshing,
        onRefresh = onRefresh,
        state = pullState,
        modifier = modifier
            .fillMaxSize()
            .background(if (glassEnabled) colors.background.copy(alpha = 0.94f) else colors.background),
        indicator = {
            // 轻量刷新指示：小尺寸 + 蓝青弧线，不遮挡页面
            Indicator(
                state = pullState,
                isRefreshing = state.refreshing,
                modifier = Modifier
                    .align(Alignment.TopCenter)
                    .padding(top = 8.dp),
                containerColor = colors.card,
                color = colors.accent,
            )
        },
    ) {
        Column(
            modifier = Modifier
                .fillMaxWidth()
                .verticalScroll(rememberScrollState())
                .statusBarsPadding()
                .navigationBarsPadding()
                .padding(horizontal = 20.dp),
            verticalArrangement = Arrangement.spacedBy(16.dp),
        ) {
            Spacer(Modifier.height(4.dp))
            TopBar(state, colors, onRefresh, onOpenSettings)

            val data = state.lastGood
            WarningCard(state, colors)

            if (data == null) {
                EmptyStateCard(colors, onOpenSettings)
            } else {
                SummaryCard(state, colors)
                QuotaCard("5 小时用量", "rolling", data.rolling, state, colors)
                QuotaCard("本周用量", "weekly", data.weekly, state, colors)
                QuotaCard("本月用量", "monthly", data.monthly, state, colors)
            }

            Spacer(Modifier.height(20.dp))
        }
    }
}

// ---------- 顶部：两层结构 + 统一 40dp 图标点击区 ----------

@Composable
private fun TopBar(state: QuotaState, colors: IosColors, onRefresh: () -> Unit, onOpenSettings: () -> Unit) {
    Row(verticalAlignment = Alignment.CenterVertically) {
        Column(Modifier.weight(1f)) {
            Text("TokenDock", fontSize = 22.sp, fontWeight = FontWeight.SemiBold, color = colors.label)
            Spacer(Modifier.height(4.dp))
            Row(verticalAlignment = Alignment.CenterVertically) {
                Box(Modifier.size(8.dp).background(stateTint(state, colors), CircleShape))
                Spacer(Modifier.width(6.dp))
                Text(statusLine(state), fontSize = 13.sp, color = colors.secondaryLabel)
            }
        }
        IconButton(onClick = onRefresh, modifier = Modifier.size(40.dp)) {
            Icon(Icons.Outlined.Refresh, contentDescription = "立即刷新", tint = colors.accent, modifier = Modifier.size(20.dp))
        }
        Spacer(Modifier.width(4.dp))
        IconButton(onClick = onOpenSettings, modifier = Modifier.size(40.dp)) {
            Icon(Icons.Outlined.Settings, contentDescription = "设置", tint = colors.accent, modifier = Modifier.size(20.dp))
        }
    }
}

// ---------- 轻量内联警告卡（失败 / 过期时） ----------

@Composable
private fun WarningCard(state: QuotaState, colors: IosColors) {
    if (!state.isStale && state.failure == FetchFailureKind.None) return
    val tint = if (state.isStale) colors.amber else colors.red
    val title = if (state.isStale) "数据已过期" else "获取失败"
    val reason = state.statusText
        .removePrefix("⚠ 数据已过期 · ")
        .removePrefix("✖ 获取失败：")
        .substringBefore(" · 最后成功")

    IosCard {
        Row {
            Icon(Icons.Outlined.WarningAmber, contentDescription = null, tint = tint, modifier = Modifier.size(20.dp))
            Spacer(Modifier.width(12.dp))
            Column {
                Text(title, fontSize = 15.sp, fontWeight = FontWeight.SemiBold, color = colors.label)
                Spacer(Modifier.height(3.dp))
                Text(reason, fontSize = 13.sp, color = colors.secondaryLabel)
                if (state.lastGood != null) {
                    Spacer(Modifier.height(3.dp))
                    Text(
                        "最近成功 ${DisplayFormat.formatClock(state.lastGood.fetchedAtMillis)}",
                        fontSize = 13.sp,
                        color = colors.tertiaryLabel,
                    )
                }
            }
        }
    }
}

// ---------- 订阅摘要卡 ----------

@Composable
private fun SummaryCard(state: QuotaState, colors: IosColors) {
    val data = state.lastGood ?: return
    val (pillText, pillTint) = when {
        state.isStale -> "已过期" to colors.amber
        state.failure != FetchFailureKind.None -> "异常" to colors.red
        else -> "正常" to colors.green
    }

    IosCard {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Column(Modifier.weight(1f)) {
                Text("OpenCode Go", fontSize = 17.sp, fontWeight = FontWeight.SemiBold, color = colors.label)
                Spacer(Modifier.height(2.dp))
                Text("官方接口 · 账号级数据", fontSize = 13.sp, color = colors.secondaryLabel)
            }
            IosStatusPill(pillText, pillTint)
        }
        Spacer(Modifier.height(10.dp))
        Text(
            "最近刷新 ${DisplayFormat.formatClock(data.fetchedAtMillis)} · 每 60 秒自动刷新",
            fontSize = 13.sp,
            color = colors.tertiaryLabel,
        )
    }
}

// ---------- 余量卡：大数字 + 条形进度 + 倒计时，点卡展开详情 ----------

@Composable
private fun QuotaCard(
    title: String,
    scopeKey: String,
    window: UsageWindow?,
    state: QuotaState,
    colors: IosColors,
) {
    var expanded by remember { mutableStateOf(false) }
    val remaining = window?.remainingPercent
    val color = valueColor(remaining, state, colors)

    IosCard(onClick = { expanded = !expanded }) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Text(title, fontSize = 15.sp, fontWeight = FontWeight.Medium, color = colors.label)
            Spacer(Modifier.weight(1f))
            Text(statusWord(window), fontSize = 13.sp, color = colors.secondaryLabel)
        }

        Spacer(Modifier.height(8.dp))

        Row(verticalAlignment = Alignment.Bottom) {
            Text(
                text = remaining?.let { DisplayFormat.formatRemainingPercent(it).removeSuffix("%") } ?: "—",
                fontSize = 34.sp,
                fontWeight = FontWeight.Bold,
                color = color,
            )
            if (remaining != null) {
                Text(
                    "%",
                    fontSize = 15.sp,
                    color = color,
                    modifier = Modifier.padding(start = 2.dp, bottom = 4.dp),
                )
            }
        }

        Spacer(Modifier.height(10.dp))

        IosBar(progress = remaining?.let { (it / 100.0).toFloat() }, color = color)

        Spacer(Modifier.height(10.dp))

        Row(verticalAlignment = Alignment.CenterVertically) {
            Text(
                text = window?.let { DisplayFormat.formatCountdown(it.resetsAtMillis) } ?: "接口未返回该窗口",
                fontSize = 13.sp,
                color = colors.secondaryLabel,
            )
            Spacer(Modifier.weight(1f))
            Icon(
                if (expanded) Icons.Outlined.ExpandLess else Icons.Outlined.ExpandMore,
                contentDescription = if (expanded) "收起" else "详情",
                tint = colors.tertiaryLabel,
                modifier = Modifier.size(20.dp),
            )
        }

        AnimatedVisibility(visible = expanded) {
            Column {
                Spacer(Modifier.height(12.dp))
                IosSeparator(startInset = 0.dp)
                Spacer(Modifier.height(12.dp))
                DetailLine("接口状态", window?.status ?: "未返回", colors)
                DetailLine("重置时间", window?.resetsAtMillis?.let { DisplayFormat.formatDateTime(it) } ?: "未返回", colors)
                DetailLine("已用百分比", window?.percentUsed?.let { "${it.toInt()}%" } ?: "未返回", colors)
                DetailLine("计算口径", "剩余 = 100 − 已用（缺失即未知）", colors)
                DetailLine("数据来源", "GET /zen/go/v1/usage · $scopeKey", colors)
            }
        }
    }
}

@Composable
private fun DetailLine(label: String, value: String, colors: IosColors) {
    Row(Modifier.padding(vertical = 3.dp)) {
        Text(label, fontSize = 13.sp, color = colors.tertiaryLabel, modifier = Modifier.width(84.dp))
        Text(value, fontSize = 13.sp, color = colors.secondaryLabel)
    }
}

// ---------- 空状态（尚未配置密钥） ----------

@Composable
private fun EmptyStateCard(colors: IosColors, onOpenSettings: () -> Unit) {
    IosCard {
        Column(
            modifier = Modifier.fillMaxWidth(),
            horizontalAlignment = Alignment.CenterHorizontally,
        ) {
            Box(
                modifier = Modifier
                    .size(56.dp)
                    .background(colors.accent.copy(alpha = 0.12f), CircleShape),
                contentAlignment = Alignment.Center,
            ) {
                Icon(Icons.Outlined.Key, contentDescription = null, tint = colors.accent, modifier = Modifier.size(26.dp))
            }
            Spacer(Modifier.height(14.dp))
            Text("尚未设置 API Key", fontSize = 17.sp, fontWeight = FontWeight.SemiBold, color = colors.label)
            Spacer(Modifier.height(6.dp))
            Text(
                "填入 OpenCode 控制台生成的 API Key，即可查看账号级余量；密钥由系统 Keystore 加密保存在本机。",
                fontSize = 13.sp,
                color = colors.secondaryLabel,
                textAlign = TextAlign.Center,
            )
            Spacer(Modifier.height(16.dp))
            Button(
                onClick = onOpenSettings,
                modifier = Modifier
                    .fillMaxWidth()
                    .height(46.dp),
                shape = RoundedCornerShape(14.dp),
                colors = ButtonDefaults.buttonColors(containerColor = colors.accent, contentColor = Color.White),
            ) {
                Text("去设置密钥", fontSize = 15.sp, fontWeight = FontWeight.SemiBold)
            }
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
        else -> "正常 · 更新于 ${DisplayFormat.formatClock(data.fetchedAtMillis)}"
    }
}

private fun stateTint(state: QuotaState, colors: IosColors): Color {
    val data = state.lastGood
    return when {
        state.refreshing -> colors.tertiaryLabel
        data == null -> if (state.failure == FetchFailureKind.NotConfigured) colors.tertiaryLabel else colors.red
        state.isStale -> colors.amber
        else -> colors.green
    }
}

/** 数字配色：正常=蓝青主色，≤20%=警告琥珀，过期=次要灰，未知=三级灰。 */
private fun valueColor(remaining: Double?, state: QuotaState, colors: IosColors): Color = when {
    state.isStale -> colors.secondaryLabel
    remaining == null -> colors.tertiaryLabel
    remaining <= 20.0 -> colors.amber
    else -> colors.accent
}

/** 卡片右侧状态文字：显示该窗口自身的接口状态（全局过期由警告卡统一提示，避免三处重复）。 */
private fun statusWord(window: UsageWindow?): String = when {
    window == null -> "接口未返回"
    window.remainingPercent == null -> "数值未知"
    else -> DisplayFormat.formatStatus(window.status)
}
