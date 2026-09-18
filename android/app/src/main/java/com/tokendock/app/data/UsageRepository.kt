package com.tokendock.app.data

import android.content.Context
import com.tokendock.app.widget.UsageWidgetUpdater
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

/**
 * 唯一数据源：前台界面、后台 Worker、桌面小组件都通过这里取数据，
 * 保证同一份官方额度不会出现两套值（不额外发起请求）。
 */
object UsageRepository {

    private val client = UsageApiClient()
    private val _state = MutableStateFlow(QuotaState())
    val state: StateFlow<QuotaState> = _state.asStateFlow()

    private var initialized = false

    /** 冷启动：先把本地缓存（上次成功数据）灌进状态，避免白屏。 */
    fun initialize(context: Context) {
        if (initialized) return
        initialized = true
        _state.value = UsageCacheStore.read(context)
    }

    /** 拉取一次官方额度并更新状态 / 缓存 / 小组件。 */
    suspend fun refresh(context: Context): QuotaState {
        _state.value = _state.value.copy(refreshing = true)
        val result = client.fetch(SecureKeyStore.load(context))
        val next = applyResult(_state.value, result)
        _state.value = next
        UsageCacheStore.write(context, next)
        UsageWidgetUpdater.requestUpdate(context)
        return next
    }

    /** 仅更新内存状态（测试 / 预览用）。 */
    fun setForTest(state: QuotaState) {
        initialized = true
        _state.value = state
    }

    /**
     * 应用一次拉取结果（纯函数，便于单测）：
     * 成功 → 覆盖数据并清除过期标记；失败 → 保留最后一次成功数据并标记「已过期」，
     * 从未成功过则只显示失败原因（不显示 0/100%）。
     */
    fun applyResult(current: QuotaState, result: FetchResult): QuotaState = when (result) {
        is FetchResult.Ok -> QuotaState(
            lastGood = result.data,
            isStale = false,
            statusText = "✔ 已更新 · ${DisplayFormat.formatClock(result.data.fetchedAtMillis)}",
            failure = FetchFailureKind.None,
        )

        is FetchResult.Fail -> {
            if (current.lastGood == null) {
                QuotaState(
                    lastGood = null,
                    isStale = false,
                    statusText = "✖ 获取失败：${result.message}",
                    failure = result.kind,
                )
            } else {
                current.copy(
                    isStale = true,
                    statusText = "⚠ 数据已过期 · ${result.message} · 最后成功：${
                        DisplayFormat.formatClock(current.lastGood.fetchedAtMillis)
                    }",
                    failure = result.kind,
                )
            }
        }
    }
}
