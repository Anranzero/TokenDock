package com.tokendock.app.work

import android.content.Context
import androidx.work.Constraints
import androidx.work.CoroutineWorker
import androidx.work.ExistingPeriodicWorkPolicy
import androidx.work.NetworkType
import androidx.work.PeriodicWorkRequestBuilder
import androidx.work.WorkManager
import androidx.work.WorkerParameters
import com.tokendock.app.data.UsageRepository
import java.util.concurrent.TimeUnit

/**
 * 后台定时刷新（WorkManager 周期任务，最小间隔 15 分钟——系统限制，与前台 60 秒无关）。
 * 失败不重试轰炸：结果由仓库统一处理（保留旧数据并标记过期），下次周期自然重试。
 */
class UsageRefreshWorker(
    context: Context,
    params: WorkerParameters,
) : CoroutineWorker(context, params) {

    override suspend fun doWork(): Result {
        val state = UsageRepository.refresh(applicationContext)
        LowQuotaNotifier.maybeNotify(applicationContext, state)
        return Result.success()
    }

    companion object {
        private const val WORK_NAME = "tokendock_usage_refresh"
        const val INTERVAL_MINUTES = 15L

        fun schedule(context: Context) {
            val request = PeriodicWorkRequestBuilder<UsageRefreshWorker>(INTERVAL_MINUTES, TimeUnit.MINUTES)
                .setConstraints(
                    Constraints.Builder()
                        .setRequiredNetworkType(NetworkType.CONNECTED)
                        .build(),
                )
                .build()
            WorkManager.getInstance(context.applicationContext).enqueueUniquePeriodicWork(
                WORK_NAME,
                ExistingPeriodicWorkPolicy.UPDATE,
                request,
            )
        }

        fun cancel(context: Context) {
            WorkManager.getInstance(context.applicationContext).cancelUniqueWork(WORK_NAME)
        }
    }
}
