# TokenDock · AI 用量助手（Android）

TokenDock 的 Android 端：原生 Kotlin + Jetpack Compose（**不使用 WebView**），
与桌面版共用同一套业务规则与数据口径，但**不搬运 C# 代码**、不读取电脑上的任何本地数据库。

当前版本聚焦 **OpenCode Go**：官方额度查询 + 低余量提醒 + 桌面小组件。

## 功能

| 模块 | 说明 |
| --- | --- |
| 官方额度 | `GET https://opencode.ai/zen/go/v1/usage`（Bearer 鉴权），显示 5 小时 / 本周 / 本月剩余百分比、进度条、状态、重置倒计时、最后更新时间 |
| 刷新 | 前台按设置间隔自动刷新（默认 60 秒）；后台用 WorkManager 周期任务（系统最小 15 分钟） |
| 通知 | 任一窗口剩余 ≤ 阈值（默认 20%）时提醒一次，同一重置周期不重复；Android 13+ 需授予通知权限 |
| 密钥安全 | Android Keystore（AES-256/GCM）加密，密文存应用私有 SharedPreferences；明文不落盘、不进日志、不上传第三方 |
| 异常处理 | 401 / 402 / 403 / 429 / 5xx / 断网 / 格式异常分别提示；失败保留最后一次成功数据并标记「数据已过期」，从不显示为 0 或 100% |
| 桌面小组件 | 5 小时剩余、本周剩余、下次重置时间；点击进入 App；后台更新失败时显示「上次更新 HH:mm · 数据已过期」 |
| 外观 | 浅色 / 暗色 / 跟随系统；Android 12+ 可选动态颜色（默认仍为 TokenDock 蓝青色）；毛玻璃（半透明卡片 + Android 12+ 窗口模糊），低内存设备自动关闭 |

### Token 统计与官方额度严格分开

- Android 端**无法**读取电脑上 OpenCode / Zcode 的本地数据库；
- 官方接口未提供账号级逐模型 Token 明细时，界面直接显示「官方暂未提供 Token 明细」；
- **不根据余量百分比反推 Token**，也不伪造跨设备 Token 总量；
- 后续版本预留「连接 TokenDock 桌面端同步」的接入点，第一版不含任何后端服务。

## 环境要求

- Android Studio（Koala 或更新）/ 或命令行 + JDK 17
- Android SDK Platform 34、Build-Tools 34+
- Gradle 由 wrapper 自动下载（8.9）

## 构建

```bash
# Debug APK（默认）
cd android
./gradlew :app:assembleDebug
# 产物：android/app/build/outputs/apk/debug/app-debug.apk

# 单元测试（纯 JVM：解析 / 口径 / 异常分类 / 倒计时）
./gradlew :app:testDebugUnitTest
```

### Release 构建

未配置签名时 `assembleRelease` 产出未签名 APK（`android/app/build/outputs/apk/release/app-release-unsigned.apk`）。
正式分发请先生成 keystore 并配置签名：

```bash
keytool -genkeypair -v -keystore tokendock-release.jks -alias tokendock \
  -keyalg RSA -keysize 2048 -validity 10000
```

把以下内容加入 `android/keystore.properties`（**不要提交到 git**）：

```properties
storeFile=../tokendock-release.jks
storePassword=******
keyAlias=tokendock
keyPassword=******
```

并在 `app/build.gradle.kts` 的 `android { }` 中追加：

```kotlin
signingConfigs {
    create("release") {
        val props = java.util.Properties().apply {
            rootProject.file("keystore.properties").inputStream().use { load(it) }
        }
        storeFile = rootProject.file(props.getProperty("storeFile"))
        storePassword = props.getProperty("storePassword")
        keyAlias = props.getProperty("keyAlias")
        keyPassword = props.getProperty("keyPassword")
    }
}
buildTypes { getByName("release") { signingConfig = signingConfigs.getByName("release") } }
```

然后：

```bash
./gradlew :app:assembleRelease
```

## 工程结构

```
android/app/src/main/java/com/tokendock/app/
├── MainActivity.kt              # 单 Activity 宿主（首页 / 设置切换、窗口模糊、调度注册）
├── data/
│   ├── UsageModels.kt           # 数据模型 + 中文格式化（与桌面端同口径）
│   ├── UsageApiClient.kt        # 官方额度客户端（状态码分类、JSON 解析、不编造数据）
│   ├── SecureKeyStore.kt        # Android Keystore(AES-GCM) 密钥加密存储
│   ├── SettingsStore.kt         # 设置（刷新间隔/阈值/主题/毛玻璃/通知/去重状态）
│   ├── UsageCacheStore.kt       # 最近成功数据缓存（冷启动与小组件共用）
│   └── UsageRepository.kt       # 唯一数据源（前台/后台/小组件共用，失败保留旧数据）
├── work/
│   ├── UsageRefreshWorker.kt    # WorkManager 周期刷新
│   └── LowQuotaNotifier.kt      # 低余量通知（同周期去重）
├── widget/                      # Glance 桌面小组件
└── ui/                          # Compose：首页 / 设置 / 主题（蓝青 + 动态颜色 + 毛玻璃）
```

## 与桌面端的关系

- 复用：数据口径（剩余 = 100 − 已用，缺失即「未知」）、失败分类与文案、低余量提醒规则、缓存与过期标记策略。
- 不共用：桌面端 C# 代码、本机会话库扫描、Token 明细统计（Android 端无此数据源）。

## 已知限制

- **targetSdk 34**：本机 SDK 现有 Platform 34，AGP 8.7.3 要求 Gradle 8.9；如需 targetSdk 36（Android 16），需安装 Platform 36 并升级 AGP 至 8.9+（`compileSdk = 36; targetSdk = 36`）。
- 后台刷新受 WorkManager 最小 15 分钟周期限制，无法做到分钟级；分钟级刷新仅在前台。
- 窗口背景模糊（毛玻璃）在部分 OEM 系统/低内存设备上不可用，此时自动降级为半透明卡片。
- Token 明细、Codex / GLM 页签为后续版本内容（当前版本聚焦 OpenCode Go）。
