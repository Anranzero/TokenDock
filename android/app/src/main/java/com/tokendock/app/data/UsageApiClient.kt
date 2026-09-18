package com.tokendock.app.data

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import org.json.JSONException
import org.json.JSONObject
import java.io.IOException
import java.net.HttpURLConnection
import java.net.URL

/**
 * OpenCode Go 官方额度客户端：GET https://opencode.ai/zen/go/v1/usage（Bearer 鉴权）。
 *
 * 仅读取真实返回的 usage.rolling / weekly / monthly 中的 percent、status、resetsAt；
 * 不推断任何 Token 数或额度数字——接口没返回的一律为 null（界面显示「未知」）。
 */
class UsageApiClient(private val endpoint: String = DEFAULT_ENDPOINT) {

    suspend fun fetch(apiKey: String?): FetchResult = withContext(Dispatchers.IO) {
        if (apiKey.isNullOrBlank()) {
            return@withContext FetchResult.Fail(
                FetchFailureKind.NotConfigured,
                "尚未设置 API 密钥，请在设置中填写。",
            )
        }

        val connection = try {
            (URL(endpoint).openConnection() as HttpURLConnection).apply {
                requestMethod = "GET"
                connectTimeout = 10_000
                readTimeout = 15_000
                setRequestProperty("Authorization", "Bearer $apiKey")
                setRequestProperty("Accept", "application/json")
                setRequestProperty("User-Agent", "TokenDock-Android/1.0")
            }
        } catch (e: IOException) {
            return@withContext FetchResult.Fail(FetchFailureKind.NetworkError, "网络连接失败：${e.message}")
        } catch (e: Exception) {
            return@withContext FetchResult.Fail(FetchFailureKind.NetworkError, "请求出现异常：${e.javaClass.simpleName}")
        }

        try {
            val code = connection.responseCode
            val body = (if (code in 200..299) connection.inputStream else connection.errorStream)
                ?.bufferedReader()?.use { it.readText() } ?: ""
            classify(code, body)
        } catch (e: IOException) {
            FetchResult.Fail(FetchFailureKind.NetworkError, "读取响应失败：${e.message}")
        } finally {
            connection.disconnect()
        }
    }

    companion object {
        const val DEFAULT_ENDPOINT = "https://opencode.ai/zen/go/v1/usage"

        /** 按状态码归类（与桌面端同一套语义）。 */
        fun classify(statusCode: Int, body: String): FetchResult {
            val detail = extractServerMessage(body)
            return when (statusCode) {
                200 -> parseSuccessBody(body)
                401 -> FetchResult.Fail(FetchFailureKind.InvalidKey, "API 密钥无效或已失效（HTTP 401），请重新设置。", detail)
                402 -> FetchResult.Fail(FetchFailureKind.NoSubscription, "订阅不可用（HTTP 402），请确认 OpenCode Go 订阅状态。", detail)
                403 -> FetchResult.Fail(FetchFailureKind.NoSubscription, "无权访问该接口（HTTP 403），可能尚未订阅或权限不足。", detail)
                429 -> FetchResult.Fail(FetchFailureKind.RateLimited, "请求过于频繁（HTTP 429），请稍后再试。", detail)
                else -> if (statusCode >= 500) {
                    FetchResult.Fail(FetchFailureKind.ServerError, "服务端异常（HTTP $statusCode），请稍后重试。", detail)
                } else {
                    FetchResult.Fail(FetchFailureKind.ServerError, "接口返回异常状态码（HTTP $statusCode）。", detail)
                }
            }
        }

        /** 解析 HTTP 200 响应体；格式不符时返回 ParseError（不编造数据）。 */
        fun parseSuccessBody(body: String): FetchResult {
            val root = try {
                JSONObject(body.ifBlank { "null" })
            } catch (e: JSONException) {
                return FetchResult.Fail(FetchFailureKind.ParseError, "接口返回内容不是有效的 JSON。", snip(body))
            }

            val usage = root.optJSONObject("usage")
                ?: return FetchResult.Fail(
                    FetchFailureKind.ParseError,
                    "接口返回格式不符合预期（缺少 usage 字段）。",
                    snip(body),
                )

            val rolling = parseWindow(usage, "rolling")
            val weekly = parseWindow(usage, "weekly")
            val monthly = parseWindow(usage, "monthly")
            if (rolling == null && weekly == null && monthly == null) {
                return FetchResult.Fail(
                    FetchFailureKind.ParseError,
                    "接口返回中缺少 rolling/weekly/monthly 用量数据，可能当前无订阅，或接口格式已变更。",
                    snip(body),
                )
            }

            return FetchResult.Ok(
                UsageData(
                    rolling = rolling,
                    weekly = weekly,
                    monthly = monthly,
                    fetchedAtMillis = System.currentTimeMillis(),
                ),
            )
        }

        private fun parseWindow(usage: JSONObject, name: String): UsageWindow? {
            val window = usage.optJSONObject(name) ?: return null
            val percent = when {
                window.has("percent") && !window.isNull("percent") -> {
                    val raw = window.opt("percent")
                    when (raw) {
                        is Number -> raw.toDouble()
                        is String -> raw.toDoubleOrNull()
                        else -> null
                    }
                }
                else -> null
            }
            val status = window.optString("status", "").ifBlank { null }
            val resetsAt = window.opt("resetsAt")?.let { value ->
                when (value) {
                    is String -> parseIsoMillis(value)
                    is Number -> value.toLong()
                    else -> null
                }
            }
            return UsageWindow(percentUsed = percent, status = status, resetsAtMillis = resetsAt)
        }

        /** 解析 ISO8601（形如 2026-09-17T12:49:17.815Z）；minSdk 28 起 java.time 可直接使用。 */
        fun parseIsoMillis(text: String): Long? = try {
            java.time.Instant.parse(text).toEpochMilli()
        } catch (e: Exception) {
            null
        }

        /** 从错误体提取 error.message（实测形态 {"type":"error","error":{"type":"AuthError","message":"..."}}）。 */
        fun extractServerMessage(body: String): String? = try {
            JSONObject(body).optJSONObject("error")?.optString("message")?.ifBlank { null }
        } catch (e: JSONException) {
            null
        }

        private fun snip(body: String): String? = when {
            body.isBlank() -> null
            body.length <= 200 -> body
            else -> body.take(200) + "…"
        }
    }
}
