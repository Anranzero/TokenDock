package com.tokendock.app.data

import android.content.Context
import android.os.Build
import org.json.JSONObject

/** 主题模式（与桌面端一致：浅色 / 暗色 / 跟随系统）。 */
enum class ThemeMode { System, Light, Dark }

/**
 * 应用设置（非敏感，存应用私有 SharedPreferences）。
 * 敏感数据只有 API Key，走 [SecureKeyStore] 的 Keystore 加密通道。
 */
object SettingsStore {

    private const val PREFS_NAME = "tokendock_settings"
    private const val KEY_REFRESH_SECONDS = "refresh_seconds"
    private const val KEY_THRESHOLD = "low_threshold"
    private const val KEY_THEME = "theme_mode"
    private const val KEY_GLASS = "glass_enabled"
    private const val KEY_DYNAMIC_COLOR = "dynamic_color"
    private const val KEY_NOTIFICATIONS = "notifications_enabled"
    private const val KEY_NOTIFY_STATE = "notify_state"

    const val DEFAULT_REFRESH_SECONDS = 60
    const val DEFAULT_THRESHOLD = 20

    private fun prefs(context: Context) =
        context.applicationContext.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)

    /** 前台刷新间隔（秒）；后台刷新受 WorkManager 最小 15 分钟限制，与此值无关。 */
    fun refreshSeconds(context: Context): Int =
        prefs(context).getInt(KEY_REFRESH_SECONDS, DEFAULT_REFRESH_SECONDS).coerceIn(30, 3600)

    fun setRefreshSeconds(context: Context, seconds: Int) {
        prefs(context).edit().putInt(KEY_REFRESH_SECONDS, seconds.coerceIn(30, 3600)).apply()
    }

    /** 低余量提醒阈值（百分比，任一无窗口低于该值时提醒一次）。 */
    fun lowThreshold(context: Context): Int =
        prefs(context).getInt(KEY_THRESHOLD, DEFAULT_THRESHOLD).coerceIn(1, 90)

    fun setLowThreshold(context: Context, percent: Int) {
        prefs(context).edit().putInt(KEY_THRESHOLD, percent.coerceIn(1, 90)).apply()
    }

    fun themeMode(context: Context): ThemeMode = when (prefs(context).getString(KEY_THEME, "system")) {
        "light" -> ThemeMode.Light
        "dark" -> ThemeMode.Dark
        else -> ThemeMode.System
    }

    fun setThemeMode(context: Context, mode: ThemeMode) {
        val value = when (mode) {
            ThemeMode.Light -> "light"
            ThemeMode.Dark -> "dark"
            ThemeMode.System -> "system"
        }
        prefs(context).edit().putString(KEY_THEME, value).apply()
    }

    /** 毛玻璃/半透明：低内存设备或无系统模糊支持时自动关闭。 */
    fun glassEnabled(context: Context): Boolean =
        prefs(context).getBoolean(KEY_GLASS, true) && isGlassSupported(context)

    fun setGlassEnabled(context: Context, enabled: Boolean) {
        prefs(context).edit().putBoolean(KEY_GLASS, enabled).apply()
    }

    /** 用户是否显式开启过毛玻璃（与设备能力无关，用于设置页回显）。 */
    fun glassPreference(context: Context): Boolean = prefs(context).getBoolean(KEY_GLASS, true)

    /** 设备是否具备毛玻璃能力：Android 12+ 且有窗口模糊，且非低内存设备。 */
    fun isGlassSupported(context: Context): Boolean {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.S) return false
        val activityManager = context.getSystemService(Context.ACTIVITY_SERVICE) as? android.app.ActivityManager
        if (activityManager?.isLowRamDevice == true) return false
        return true
    }

    fun dynamicColorEnabled(context: Context): Boolean =
        prefs(context).getBoolean(KEY_DYNAMIC_COLOR, false) && Build.VERSION.SDK_INT >= Build.VERSION_CODES.S

    fun setDynamicColorEnabled(context: Context, enabled: Boolean) {
        prefs(context).edit().putBoolean(KEY_DYNAMIC_COLOR, enabled).apply()
    }

    fun notificationsEnabled(context: Context): Boolean =
        prefs(context).getBoolean(KEY_NOTIFICATIONS, true)

    fun setNotificationsEnabled(context: Context, enabled: Boolean) {
        prefs(context).edit().putBoolean(KEY_NOTIFICATIONS, enabled).apply()
    }

    /** 低余量提醒去重：记录上次提醒的 (窗口, 重置时间)，同一周期只提醒一次。 */
    fun notifiedCycles(context: Context): Map<String, Long> {
        val raw = prefs(context).getString(KEY_NOTIFY_STATE, null) ?: return emptyMap()
        return try {
            val json = JSONObject(raw)
            json.keys().asSequence().associateWith { json.optLong(it) }
        } catch (e: Exception) {
            emptyMap()
        }
    }

    fun setNotifiedCycles(context: Context, cycles: Map<String, Long>) {
        val json = JSONObject()
        cycles.forEach { (key, value) -> json.put(key, value) }
        prefs(context).edit().putString(KEY_NOTIFY_STATE, json.toString()).apply()
    }
}
