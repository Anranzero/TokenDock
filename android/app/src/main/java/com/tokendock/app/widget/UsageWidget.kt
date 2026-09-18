package com.tokendock.app.widget

import android.content.Context
import android.content.Intent
import androidx.compose.runtime.Composable
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.glance.GlanceId
import androidx.glance.GlanceModifier
import androidx.glance.action.clickable
import androidx.glance.appwidget.GlanceAppWidget
import androidx.glance.appwidget.action.actionStartActivity
import androidx.glance.appwidget.provideContent
import androidx.glance.background
import androidx.glance.layout.Alignment
import androidx.glance.layout.Box
import androidx.glance.layout.Column
import androidx.glance.layout.Row
import androidx.glance.layout.Spacer
import androidx.glance.layout.fillMaxSize
import androidx.glance.layout.height
import androidx.glance.layout.padding
import androidx.glance.layout.size
import androidx.glance.layout.width
import androidx.glance.text.FontWeight
import androidx.glance.text.Text
import androidx.glance.text.TextStyle
import androidx.glance.unit.ColorProvider
import androidx.compose.ui.platform.LocalContext
import com.tokendock.app.MainActivity
import com.tokendock.app.R
import com.tokendock.app.data.DisplayFormat
import com.tokendock.app.data.FetchFailureKind
import com.tokendock.app.data.QuotaState
import com.tokendock.app.data.UsageCacheStore

/**
 * 桌面小组件：显示 5 小时剩余、本周剩余、下次重置时间；点击进入 App。
 * 数据与 App 同一份本地缓存（不单独请求）；后台更新失败时显示上次更新时间并标记已过期。
 * 配色走资源（values / values-night），自动跟随系统深浅色。
 */
class UsageWidget : GlanceAppWidget() {

    override suspend fun provideGlance(context: Context, id: GlanceId) {
        val state = UsageCacheStore.read(context)
        provideContent { UsageWidgetContent(state) }
    }
}

@Composable
private fun UsageWidgetContent(state: QuotaState) {
    val data = state.lastGood
    val context = LocalContext.current
    val openApp = actionStartActivity(
        Intent(context, MainActivity::class.java).apply {
            flags = Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TOP
        },
    )

    Column(
        modifier = GlanceModifier
            .fillMaxSize()
            .background(ColorProvider(R.color.widget_bg))
            .padding(14.dp)
            .clickable(openApp),
        verticalAlignment = Alignment.Vertical.CenterVertically,
    ) {
        Row(verticalAlignment = Alignment.Vertical.CenterVertically) {
            Box(
                modifier = GlanceModifier
                    .size(8.dp)
                    .background(ColorProvider(R.color.widget_accent)),
            ) {}
            Spacer(GlanceModifier.width(6.dp))
            Text(
                text = "TokenDock",
                style = TextStyle(
                    color = ColorProvider(R.color.widget_text_primary),
                    fontSize = 13.sp,
                    fontWeight = FontWeight.Medium,
                ),
            )
        }

        Spacer(GlanceModifier.height(10.dp))

        WidgetLine("5 小时剩余", data?.rolling?.remainingPercent)
        Spacer(GlanceModifier.height(6.dp))
        WidgetLine("本周剩余", data?.weekly?.remainingPercent)

        Spacer(GlanceModifier.height(10.dp))

        if (data == null) {
            Text(
                text = if (state.failure == FetchFailureKind.NotConfigured) "未设置 API Key" else "尚未获取数据",
                style = TextStyle(
                    color = ColorProvider(R.color.widget_text_secondary),
                    fontSize = 11.sp,
                ),
            )
        } else {
            Text(
                text = DisplayFormat.formatCountdown(data.rolling?.resetsAtMillis ?: data.weekly?.resetsAtMillis),
                style = TextStyle(
                    color = ColorProvider(R.color.widget_text_primary),
                    fontSize = 12.sp,
                ),
            )
            Spacer(GlanceModifier.height(4.dp))
            Text(
                text = if (state.isStale) {
                    "上次更新 ${DisplayFormat.formatClock(data.fetchedAtMillis)} · 数据已过期"
                } else {
                    "更新于 ${DisplayFormat.formatClock(data.fetchedAtMillis)}"
                },
                style = TextStyle(
                    color = ColorProvider(if (state.isStale) R.color.widget_warn else R.color.widget_text_secondary),
                    fontSize = 10.sp,
                ),
            )
        }
    }
}

@Composable
private fun WidgetLine(label: String, remainingPercent: Double?) {
    Row(verticalAlignment = Alignment.Vertical.CenterVertically) {
        Text(
            text = label,
            style = TextStyle(
                color = ColorProvider(R.color.widget_text_secondary),
                fontSize = 11.sp,
            ),
        )
        Spacer(GlanceModifier.defaultWeight())
        Text(
            text = DisplayFormat.formatRemainingPercent(remainingPercent),
            style = TextStyle(
                color = ColorProvider(R.color.widget_text_primary),
                fontSize = 16.sp,
                fontWeight = FontWeight.Bold,
            ),
        )
    }
}
