package com.tokendock.app.data

import android.content.Context
import org.json.JSONObject

/**
 * 最近一次成功数据 + 失败状态的本地缓存（应用私有 SharedPreferences）。
 * 用途：冷启动立即显示、桌面小组件在后台读取、失败时保留旧数据并标记「已过期」。
 * 不含任何敏感信息（只有百分比/状态/重置时间/时间戳）。
 */
object UsageCacheStore {

    private const val PREFS_NAME = "tokendock_cache"
    private const val KEY_PAYLOAD = "quota_payload"

    fun write(context: Context, state: QuotaState) {
        val json = JSONObject()
        state.lastGood?.let { data ->
            json.put("rolling", windowJson(data.rolling))
            json.put("weekly", windowJson(data.weekly))
            json.put("monthly", windowJson(data.monthly))
            json.put("fetchedAt", data.fetchedAtMillis)
        }
        json.put("stale", state.isStale)
        json.put("statusText", state.statusText)
        json.put("failure", state.failure.name)
        prefs(context).edit().putString(KEY_PAYLOAD, json.toString()).apply()
    }

    fun read(context: Context): QuotaState {
        val raw = prefs(context).getString(KEY_PAYLOAD, null) ?: return QuotaState()
        return try {
            val json = JSONObject(raw)
            val data = json.optJSONObject("rolling")?.let { _ ->
                val fetchedAt = json.optLong("fetchedAt", 0L)
                UsageData(
                    rolling = readWindow(json.optJSONObject("rolling")),
                    weekly = readWindow(json.optJSONObject("weekly")),
                    monthly = readWindow(json.optJSONObject("monthly")),
                    fetchedAtMillis = fetchedAt,
                )
            }
            QuotaState(
                lastGood = data,
                isStale = json.optBoolean("stale", false),
                statusText = json.optString("statusText", "尚未获取数据"),
                failure = runCatching { FetchFailureKind.valueOf(json.optString("failure", "None")) }
                    .getOrDefault(FetchFailureKind.None),
            )
        } catch (e: Exception) {
            QuotaState()
        }
    }

    private fun windowJson(window: UsageWindow?): JSONObject? = window?.let {
        JSONObject().apply {
            it.percentUsed?.let { value -> put("percent", value) }
            it.status?.let { value -> put("status", value) }
            it.resetsAtMillis?.let { value -> put("resetsAt", value) }
        }
    }

    private fun readWindow(json: JSONObject?): UsageWindow? {
        json ?: return null
        val percent = if (json.has("percent")) json.optDouble("percent") else null
        return UsageWindow(
            percentUsed = percent,
            status = json.optString("status", "").ifBlank { null },
            resetsAtMillis = if (json.has("resetsAt")) json.optLong("resetsAt") else null,
        )
    }

    private fun prefs(context: Context) =
        context.applicationContext.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)
}
