package com.tokendock.app.ui

import androidx.compose.foundation.background
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
import androidx.compose.material.icons.filled.Lock
import androidx.compose.material.icons.filled.Refresh
import androidx.compose.material.icons.filled.Settings
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
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
import com.tokendock.app.ui.ios.IosColors
import com.tokendock.app.ui.ios.IosFooter
import com.tokendock.app.ui.ios.IosRing
import com.tokendock.app.ui.ios.IosRow
import com.tokendock.app.ui.ios.IosRowBlock
import com.tokendock.app.ui.ios.IosSection
import com.tokendock.app.ui.ios.IosSectionHeader
import com.tokendock.app.ui.ios.IosSeparator
import com.tokendock.app.ui.ios.iosColors

/**
 * 首页（iOS 版式）：大标题 + 状态行 → 异常横幅 → 「套餐余量」分组内嵌列表
 * （每行 = 圆环进度 + 窗口名 + 状态/重置倒计时）→ 分组页脚说明。
 * 无数据时不用占位卡片，只给一张引导分组 + 主按钮。
 */
@Composable
fun HomeScreen(
    state: QuotaState,
    glassEnabled: Boolean,
    onOpenSettings: () -> Unit,
    onRefresh: () -> Unit,
    modifier: Modifier = Modifier,
) {
    val colors = iosColors()

    Column(
        modifier = modifier
            .fillMaxWidth()
            .background(if (glassEnabled) colors.background.copy(alpha = 0.94f) else colors.background)
            .verticalScroll(rememberScrollState())
            .statusBarsPadding()
            .navigationBarsPadding(),
    ) {
        Spacer(Modifier.height(8.dp))
        LargeTitle(state, colors, onRefresh, onOpenSettings)

        if (state.lastGood == null) {
            EmptySection(state, colors, onOpenSettings)
        } else {
            val data = state.lastGood

            StateBanner(state, colors)

            IosSectionHeader("套餐余量")
            IosSection {
                QuotaRow("5 小时", data.rolling, state, colors)
                IosSeparator()
                QuotaRow("本周", data.weekly, state, colors)
                IosSeparator()
                QuotaRow("本月", data.monthly, state, colors)
            }
            IosFooter(
                if (state.isStale) {
                    "数据已过期，展示的是最后一次成功获取的数值。"
                } else {
                    "每 60 秒自动刷新；界面关闭后由系统调度（最短 15 分钟）。"
                },
            )

            IosSectionHeader("Token 明细")
            IosFooter("官方暂未提供账号级 Token 明细；Android 端不读取电脑端数据，也不按剩余百分比反推 Token。")
        }

        Spacer(Modifier.height(32.dp))
    }
}

// ---------- 大标题 ----------

@Composable
private fun LargeTitle(state: QuotaState, colors: IosColors, onRefresh: () -> Unit, onOpenSettings: () -> Unit) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(start = 20.dp, end = 8.dp, top = 8.dp, bottom = 12.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Column(Modifier.weight(1f)) {
            Text("TokenDock", fontSize = 34.sp, fontWeight = FontWeight.Bold, color = colors.label)
            Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.padding(top = 6.dp)) {
                Box(Modifier.size(8.dp).background(statusColor(state, colors), CircleShape))
                Spacer(Modifier.width(6.dp))
                Text(statusLine(state), fontSize = 15.sp, color = colors.secondaryLabel)
            }
        }
        IconButton(onClick = onRefresh) {
            Icon(Icons.Filled.Refresh, contentDescription = "立即刷新", tint = colors.accent)
        }
        IconButton(onClick = onOpenSettings) {
            Icon(Icons.Filled.Settings, contentDescription = "设置", tint = colors.accent)
        }
    }
}

/** 异常/过期横幅（iOS 风格浅色提示条）。 */
@Composable
private fun StateBanner(state: QuotaState, colors: IosColors) {
    if (!state.isStale && state.failure == FetchFailureKind.None) return
    val tint = if (state.isStale) colors.amber else colors.red
    Box(
        modifier = Modifier
            .fillMaxWidth()
            .padding(horizontal = 16.dp)
            .background(tint.copy(alpha = 0.12f), RoundedCornerShape(10.dp))
            .padding(horizontal = 14.dp, vertical = 10.dp),
    ) {
        Text(state.statusText, fontSize = 13.sp, color = tint)
    }
}

// ---------- 额度行：圆环 + 窗口名 + 倒计时 ----------

@Composable
private fun QuotaRow(title: String, window: UsageWindow?, state: QuotaState, colors: IosColors) {
    val remaining = window?.remainingPercent
    val color = valueColor(remaining, state, colors)

    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(horizontal = 16.dp, vertical = 12.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        IosRing(
            progress = remaining?.let { (it / 100.0).toFloat() },
            color = color,
            size = 52.dp,
            stroke = 5.dp,
        ) {
            Text(
                text = remaining?.let { DisplayFormat.formatRemainingPercent(it).removeSuffix("%") } ?: "—",
                fontSize = 15.sp,
                fontWeight = FontWeight.SemiBold,
                color = color,
            )
        }
        Spacer(Modifier.width(14.dp))
        Column(Modifier.weight(1f)) {
            Text(title, fontSize = 17.sp, color = colors.label)
            Text(
                text = caption(window, remaining),
                fontSize = 13.sp,
                color = colors.secondaryLabel,
            )
        }
    }
}

// ---------- 空状态：分组引导 + 主按钮 ----------

@Composable
private fun EmptySection(state: QuotaState, colors: IosColors, onOpenSettings: () -> Unit) {
    val needsKey = state.failure == FetchFailureKind.NotConfigured

    IosSectionHeader("开始使用")
    IosSection {
        IosRow(
            title = if (needsKey) "尚未设置 API Key" else "尚未获取数据",
            subtitle = if (needsKey) "在 OpenCode 控制台生成后填入即可查看余量" else state.statusText,
            icon = Icons.Filled.Lock,
            iconTint = colors.accent,
        )
        IosSeparator()
        IosRowBlock {
            Button(
                onClick = onOpenSettings,
                modifier = Modifier
                    .fillMaxWidth()
                    .height(46.dp),
                shape = RoundedCornerShape(12.dp),
                colors = ButtonDefaults.buttonColors(
                    containerColor = colors.accent,
                    contentColor = Color.White,
                ),
            ) {
                Text(if (needsKey) "去设置密钥" else "打开设置", fontSize = 17.sp, fontWeight = FontWeight.SemiBold)
            }
        }
    }
    IosFooter("额度数据仅来自官方接口 GET https://opencode.ai/zen/go/v1/usage，使用你自己的 API Key 鉴权，与官网账号数据一致。")

    IosSectionHeader("Token 明细")
    IosFooter("官方暂未提供账号级 Token 明细；Android 端不读取电脑端数据，也不按剩余百分比反推 Token。")
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

private fun statusColor(state: QuotaState, colors: IosColors): Color {
    val data = state.lastGood
    return when {
        state.refreshing -> colors.tertiaryLabel
        data == null -> if (state.failure == FetchFailureKind.NotConfigured) colors.tertiaryLabel else colors.red
        state.isStale -> colors.amber
        else -> colors.green
    }
}

/** 百分比配色：正常=主题蓝青，≤20%=琥珀，过期=次要灰，未知=三级灰。 */
private fun valueColor(remaining: Double?, state: QuotaState, colors: IosColors): Color = when {
    state.isStale -> colors.secondaryLabel
    remaining == null -> colors.tertiaryLabel
    remaining <= 20.0 -> colors.amber
    else -> colors.accent
}

private fun caption(window: UsageWindow?, remaining: Double?): String = when {
    window == null -> "接口未返回该窗口"
    remaining == null -> "接口未提供百分比 · ${DisplayFormat.formatCountdown(window.resetsAtMillis)}"
    else -> "${DisplayFormat.formatStatus(window.status)} · ${DisplayFormat.formatCountdown(window.resetsAtMillis)}"
}
