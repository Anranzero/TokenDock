package com.tokendock.app

import com.tokendock.app.data.DisplayFormat
import com.tokendock.app.data.FetchFailureKind
import com.tokendock.app.data.FetchResult
import com.tokendock.app.data.QuotaState
import com.tokendock.app.data.UsageApiClient
import com.tokendock.app.data.UsageRepository
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * 纯 JVM 单测：官方响应解析、口径（剩余 = 100 − 已用）、失败分类、失败保留旧数据。
 * 与桌面端保持同一套业务规则。
 */
class UsageParsingTest {

    private val sampleBody = """
        {"usage":{
          "rolling":{"status":"ok","percent":8,"resetsAt":"2026-09-17T12:49:17.815Z"},
          "weekly":{"status":"ok","percent":8,"resetsAt":"2026-09-21T00:00:00.815Z"},
          "monthly":{"status":"ok","percent":4,"resetsAt":"2026-10-14T06:34:05.815Z"}}}
    """.trimIndent()

    @Test
    fun parsesSuccessBodyAndComputesRemaining() {
        val result = UsageApiClient.parseSuccessBody(sampleBody)
        assertTrue(result is FetchResult.Ok)
        val data = (result as FetchResult.Ok).data
        // 剩余 = 100 − 已用（rolling/weekly = 8 → 92；monthly = 4 → 96）
        assertEquals(92.0, data.rolling!!.remainingPercent!!, 0.001)
        assertEquals(92.0, data.weekly!!.remainingPercent!!, 0.001)
        assertEquals(96.0, data.monthly!!.remainingPercent!!, 0.001)
        assertEquals("ok", data.rolling!!.status)
        assertNotNull(data.rolling!!.resetsAtMillis)
    }

    @Test
    fun missingPercentStaysNullInsteadOfZero() {
        val result = UsageApiClient.parseSuccessBody("""{"usage":{"rolling":{"status":"ok"}}}""")
        val data = (result as FetchResult.Ok).data
        assertNull(data.rolling!!.percentUsed)
        assertNull(data.rolling!!.remainingPercent) // 不显示为 0，也不显示为 100
        assertEquals("未知", DisplayFormat.formatRemainingPercent(null))
    }

    @Test
    fun remainingIsClampedBetweenZeroAndHundred() {
        val over = UsageApiClient.parseSuccessBody("""{"usage":{"rolling":{"percent":120}}}""")
        assertEquals(0.0, (over as FetchResult.Ok).data.rolling!!.remainingPercent!!, 0.001)
        val negative = UsageApiClient.parseSuccessBody("""{"usage":{"rolling":{"percent":-5}}}""")
        assertEquals(100.0, (negative as FetchResult.Ok).data.rolling!!.remainingPercent!!, 0.001)
    }

    @Test
    fun emptyUsageIsParseError() {
        val result = UsageApiClient.parseSuccessBody("""{"usage":{}}""")
        assertTrue(result is FetchResult.Fail)
        assertEquals(FetchFailureKind.ParseError, (result as FetchResult.Fail).kind)
    }

    @Test
    fun statusCodesAreClassified() {
        assertEquals(FetchFailureKind.InvalidKey, (UsageApiClient.classify(401, "{}") as FetchResult.Fail).kind)
        assertEquals(FetchFailureKind.NoSubscription, (UsageApiClient.classify(403, "{}") as FetchResult.Fail).kind)
        assertEquals(FetchFailureKind.RateLimited, (UsageApiClient.classify(429, "{}") as FetchResult.Fail).kind)
        assertEquals(FetchFailureKind.ServerError, (UsageApiClient.classify(500, "{}") as FetchResult.Fail).kind)
    }

    @Test
    fun serverMessageIsExtractedFromErrorBody() {
        val detail = UsageApiClient.extractServerMessage(
            """{"type":"error","error":{"type":"AuthError","message":"Unauthorized"}}""",
        )
        assertEquals("Unauthorized", detail)
    }

    @Test
    fun failureKeepsLastGoodDataAndMarksStale() {
        val ok = UsageApiClient.parseSuccessBody(sampleBody) as FetchResult.Ok
        val filled = QuotaState(lastGood = ok.data, isStale = false, statusText = "✔ 已更新")
        val failed = UsageRepository.applyResult(filled, FetchResult.Fail(FetchFailureKind.NetworkError, "网络连接失败"))

        assertNotNull(failed.lastGood)          // 保留最后一次成功数据
        assertTrue(failed.isStale)              // 标记已过期
        assertTrue(failed.statusText.contains("已过期"))
        assertEquals(92.0, failed.lastGood!!.rolling!!.remainingPercent!!, 0.001)
    }

    @Test
    fun failureWithoutPreviousDataNeverShowsZeroOrHundred() {
        val failed = UsageRepository.applyResult(
            QuotaState(),
            FetchResult.Fail(FetchFailureKind.InvalidKey, "API 密钥无效或已失效（HTTP 401）"),
        )
        assertNull(failed.lastGood)             // 没有数据就不显示数字
        assertTrue(!failed.isStale)
        assertTrue(failed.statusText.contains("401"))
        assertEquals("未知", DisplayFormat.formatRemainingPercent(failed.lastGood?.rolling?.remainingPercent))
    }

    @Test
    fun countdownFormattingMatchesDesktopRules() {
        val now = 1_000_000_000_000L
        assertEquals("重置时间未知", DisplayFormat.formatCountdown(null, now))
        assertEquals("2小时后重置", DisplayFormat.formatCountdown(now + 2 * 3600_000L, now))
        assertEquals("1小时30分钟后重置", DisplayFormat.formatCountdown(now + 90 * 60_000L, now))
        assertEquals("3天后重置", DisplayFormat.formatCountdown(now + 3 * 24 * 3600_000L, now))
        assertTrue(DisplayFormat.formatCountdown(now - 1000L, now).contains("已到重置时间"))
    }
}
