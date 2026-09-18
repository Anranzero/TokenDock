package com.tokendock.app

import android.os.Build
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.viewModels
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.tokendock.app.data.SettingsStore
import com.tokendock.app.data.UsageRepository
import com.tokendock.app.ui.HomeScreen
import com.tokendock.app.ui.SettingsScreen
import com.tokendock.app.ui.UsageViewModel
import com.tokendock.app.ui.theme.TokenDockTheme
import com.tokendock.app.work.LowQuotaNotifier
import com.tokendock.app.work.UsageRefreshWorker

/**
 * 单一 Activity：首页（额度卡片）与设置页通过 Compose 内部状态切换。
 * 前台按设置间隔（默认 60 秒）自动刷新，由 ViewModel 协程负责；后台交给 WorkManager 周期任务。
 */
class MainActivity : ComponentActivity() {

    private val viewModel: UsageViewModel by viewModels()

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)

        UsageRepository.initialize(applicationContext)
        LowQuotaNotifier.ensureChannel(this)
        UsageRefreshWorker.schedule(this) // 幂等：重复调用只更新周期任务配置
        applyWindowBlurIfEnabled()

        setContent {
            val context = LocalContext.current
            var themeMode by remember { mutableStateOf(SettingsStore.themeMode(context)) }
            var dynamicColor by remember { mutableStateOf(SettingsStore.dynamicColorEnabled(context)) }
            var glass by remember { mutableStateOf(SettingsStore.glassEnabled(context)) }
            var showSettings by remember { mutableStateOf(false) }

            val state by viewModel.state.collectAsStateWithLifecycle()

            TokenDockTheme(themeMode = themeMode, dynamicColor = dynamicColor) {
                Surface(
                    modifier = Modifier.fillMaxSize(),
                    color = MaterialTheme.colorScheme.background,
                ) {
                    if (showSettings) {
                        SettingsScreen(
                            onThemeChanged = {
                                themeMode = SettingsStore.themeMode(context)
                                dynamicColor = SettingsStore.dynamicColorEnabled(context)
                                glass = SettingsStore.glassEnabled(context)
                                applyWindowBlurIfEnabled()
                            },
                            onBack = { showSettings = false },
                        )
                    } else {
                        HomeScreen(
                            state = state,
                            glassEnabled = glass,
                            onOpenSettings = { showSettings = true },
                            onRefresh = { viewModel.refreshNow() },
                        )
                    }
                }
            }
        }
    }

    override fun onStart() {
        super.onStart()
        viewModel.startAutoRefresh() // 进入前台：立即刷新并按间隔轮询
    }

    override fun onStop() {
        viewModel.stopAutoRefresh() // 离开前台：停止轮询，后台交给 WorkManager
        super.onStop()
    }

    /** 毛玻璃：Android 12+ 启用窗口背景模糊；设备不支持时静默降级为半透明卡片。 */
    private fun applyWindowBlurIfEnabled() {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.S) return
        try {
            val attributes = window.attributes
            attributes.blurBehindRadius = if (SettingsStore.glassEnabled(this)) 60 else 0
            window.attributes = attributes
        } catch (e: Exception) {
            // 忽略：模糊不可用时半透明仍然生效
        }
    }
}
