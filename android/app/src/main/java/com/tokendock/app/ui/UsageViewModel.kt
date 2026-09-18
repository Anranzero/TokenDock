package com.tokendock.app.ui

import android.app.Application
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import com.tokendock.app.data.QuotaState
import com.tokendock.app.data.SettingsStore
import com.tokendock.app.data.UsageRepository
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch

/**
 * 前台刷新：按设置的间隔（默认 60 秒）自动拉取；界面进入后台时停止（后台交给 WorkManager）。
 */
class UsageViewModel(application: Application) : AndroidViewModel(application) {

    val state: StateFlow<QuotaState> = UsageRepository.state

    private var ticker: Job? = null

    /** 前台自动刷新循环（onStart 调用）。 */
    fun startAutoRefresh() {
        ticker?.cancel()
        ticker = viewModelScope.launch {
            // 立即刷一次，然后按间隔循环
            UsageRepository.refresh(getApplication())
            while (isActive) {
                val seconds = SettingsStore.refreshSeconds(getApplication())
                delay(seconds * 1000L)
                UsageRepository.refresh(getApplication())
            }
        }
    }

    /** 界面离开前台：停止轮询（后台由 WorkManager 周期任务负责）。 */
    fun stopAutoRefresh() {
        ticker?.cancel()
        ticker = null
    }

    fun refreshNow() {
        viewModelScope.launch { UsageRepository.refresh(getApplication()) }
    }

    override fun onCleared() {
        stopAutoRefresh()
        super.onCleared()
    }
}
