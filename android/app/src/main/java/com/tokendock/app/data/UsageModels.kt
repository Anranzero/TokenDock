package com.tokendock.app.data

/**
 * 单个用量窗口。与 TokenDock 桌面端保持同一业务口径：
 * - 剩余百分比 = 100 − 已用百分比，钳制在 0~100；
 * - 接口未返回 percent 时 remainingPercent 为 null（界面显示「未知」），绝不显示为 0 或 100。
 */
data class UsageWindow(
    val percentUsed: Double?,
    val status: String?,
    val resetsAtMillis: Long?,
) {
    val remainingPercent: Double?
        get() = percentUsed?.let { (100.0 - it).coerceIn(0.0, 100.0) }
}

/** 一次成功拉取到的完整用量数据（账号级，来自官方接口）。 */
data class UsageData(
    val rolling: UsageWindow?,
    val weekly: UsageWindow?,
    val monthly: UsageWindow?,
    val fetchedAtMillis: Long,
)

/** 拉取失败的分类（与桌面端一致）。 */
enum class FetchFailureKind {
    None,
    NotConfigured,
    NetworkError,
    InvalidKey,
    NoSubscription,
    RateLimited,
    ServerError,
    ParseError,
}

sealed class FetchResult {
    data class Ok(val data: UsageData) : FetchResult()

    data class Fail(
        val kind: FetchFailureKind,
        val message: String,
        val serverDetail: String? = null,
    ) : FetchResult()
}

/** 界面/小组件共用的状态：失败保留最后一次成功数据并标记「已过期」。 */
data class QuotaState(
    val lastGood: UsageData? = null,
    val isStale: Boolean = false,
    val statusText: String = "尚未获取数据",
    val failure: FetchFailureKind = FetchFailureKind.None,
    val refreshing: Boolean = false,
)

/** 中文展示格式化（与桌面端同一套文案规则）。 */
object DisplayFormat {
    fun formatRemainingPercent(remaining: Double?): String =
        remaining?.let { "${it.roundToInt()}%" } ?: "未知"

    fun formatStatus(status: String?): String = when (status?.trim()?.lowercase()) {
        null, "" -> "状态未知"
        "ok", "normal", "healthy", "active" -> "正常"
        "warn", "warning", "elevated" -> "偏高"
        "limit", "limited", "exceeded", "exhausted", "blocked", "throttled" -> "已限流"
        else -> status.trim()
    }

    /** 重置倒计时（自然中文，整点省略分钟；已过期提示等待刷新）。 */
    fun formatCountdown(resetsAtMillis: Long?, nowMillis: Long = System.currentTimeMillis()): String {
        val resetsAt = resetsAtMillis ?: return "重置时间未知"
        val remain = resetsAt - nowMillis
        if (remain <= 0) return "已到重置时间，等待刷新"
        val minutes = remain / 60_000
        val hours = minutes / 60
        val days = hours / 24
        return when {
            days >= 1 -> if (hours % 24 == 0L) "${days}天后重置" else "${days}天${hours % 24}小时后重置"
            hours >= 1 -> if (minutes % 60 == 0L) "${hours}小时后重置" else "${hours}小时${minutes % 60}分钟后重置"
            minutes >= 1 -> "${minutes}分钟后重置"
            else -> "不足1分钟后重置"
        }
    }

    fun formatClock(millis: Long): String {
        val text = java.text.SimpleDateFormat("HH:mm", java.util.Locale.getDefault())
        return text.format(java.util.Date(millis))
    }

    private fun Double.roundToInt(): Long = kotlin.math.round(this).toLong()
}
