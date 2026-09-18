package com.tokendock.app.work

import android.Manifest
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.os.Build
import androidx.core.app.NotificationCompat
import androidx.core.app.NotificationManagerCompat
import androidx.core.content.ContextCompat
import com.tokendock.app.MainActivity
import com.tokendock.app.R
import com.tokendock.app.data.DisplayFormat
import com.tokendock.app.data.QuotaState
import com.tokendock.app.data.SettingsStore
import com.tokendock.app.data.UsageWindow

/**
 * 低余量通知：任一窗口剩余 ≤ 阈值时提醒一次（同一重置周期内不重复轰炸）。
 * 通知内容只包含百分比与重置时间，不含密钥等敏感信息。
 */
object LowQuotaNotifier {

    private const val CHANNEL_ID = "quota_low"
    private const val NOTIFICATION_ID = 1001

    fun ensureChannel(context: Context) {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.O) return
        val manager = context.getSystemService(NotificationManager::class.java) ?: return
        if (manager.getNotificationChannel(CHANNEL_ID) != null) return
        manager.createNotificationChannel(
            NotificationChannel(CHANNEL_ID, "套餐低余量提醒", NotificationManager.IMPORTANCE_DEFAULT).apply {
                description = "任一额度窗口剩余低于阈值时提醒一次"
            },
        )
    }

    fun maybeNotify(context: Context, state: QuotaState) {
        if (!SettingsStore.notificationsEnabled(context)) return
        val data = state.lastGood ?: return
        ensureChannel(context)

        val threshold = SettingsStore.lowThreshold(context)
        val windows = listOf(
            "5小时窗口" to data.rolling,
            "本周" to data.weekly,
            "本月" to data.monthly,
        )

        val notified = SettingsStore.notifiedCycles(context).toMutableMap()
        var changed = false
        windows.forEach { (label, window) ->
            val cycle = window?.resetsAtMillis ?: return@forEach
            val remaining = window.remainingPercent ?: return@forEach
            if (remaining > threshold) return@forEach
            if (notified[label] == cycle) return@forEach // 本周期已提醒过

            notified[label] = cycle
            changed = true
            post(context, label, remaining, window)
        }

        if (changed) SettingsStore.setNotifiedCycles(context, notified)
    }

    /** 阈值变更 / 重新开启通知时清空去重记录，便于立即生效。 */
    fun resetDedup(context: Context) = SettingsStore.setNotifiedCycles(context, emptyMap())

    private fun post(context: Context, label: String, remaining: Double, window: UsageWindow) {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU &&
            ContextCompat.checkSelfPermission(context, Manifest.permission.POST_NOTIFICATIONS) !=
            PackageManager.PERMISSION_GRANTED
        ) {
            return // 未授权时静默跳过，不弹任何异常
        }

        val intent = Intent(context, MainActivity::class.java).apply {
            flags = Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TOP
        }
        val pending = PendingIntent.getActivity(
            context,
            0,
            intent,
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE,
        )

        val notification = NotificationCompat.Builder(context, CHANNEL_ID)
            .setSmallIcon(R.mipmap.ic_launcher)
            .setContentTitle("套餐余量不足")
            .setContentText(
                "$label 仅剩 ${DisplayFormat.formatRemainingPercent(remaining)}，${
                    DisplayFormat.formatCountdown(window.resetsAtMillis)
                }。",
            )
            .setContentIntent(pending)
            .setAutoCancel(true)
            .build()

        NotificationManagerCompat.from(context).notify(NOTIFICATION_ID, notification)
    }
}
