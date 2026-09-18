package com.tokendock.app.widget

import android.content.Context
import androidx.glance.appwidget.updateAll

/**
 * 小组件刷新入口：仓库在每次拉取后调用（成功或失败都刷新，失败时展示"上次更新时间"）。
 * 任何异常都吞掉——小组件不可用不应影响主流程。
 */
object UsageWidgetUpdater {
    suspend fun requestUpdate(context: Context) {
        try {
            UsageWidget().updateAll(context.applicationContext)
        } catch (e: Exception) {
            // 忽略：小组件未添加/系统限制时无需处理
        }
    }
}
