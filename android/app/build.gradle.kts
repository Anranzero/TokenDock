plugins {
    alias(libs.plugins.android.application)
    alias(libs.plugins.kotlin.android)
    alias(libs.plugins.kotlin.compose)
}

android {
    namespace = "com.tokendock.app"
    compileSdk = 36
    // 使用本机已安装的 Build-Tools（AGP 默认要求 35.0.0，显式指定 36.1.0 避免额外下载）
    buildToolsVersion = "36.1.0"

    defaultConfig {
        applicationId = "com.tokendock.app"
        minSdk = 28
        targetSdk = 36
        versionCode = 1
        versionName = "1.0.0"
    }

    buildTypes {
        release {
            // R8 裁剪：material-icons-extended 等大依赖在 release 中只保留用到的部分
            isMinifyEnabled = true
            isShrinkResources = true
            // 未配置签名时 assembleRelease 产出未签名 APK；配置 keystore 后自动签名。
            // 详见 android/README.md「Release 构建」。
            proguardFiles(getDefaultProguardFile("proguard-android-optimize.txt"), "proguard-rules.pro")
        }
        debug {
            // 本地调试包不裁剪，体积偏大属正常；交付以 release 为准
            isMinifyEnabled = false
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    kotlinOptions {
        jvmTarget = "17"
    }

    buildFeatures {
        compose = true
    }
}

dependencies {
    implementation(libs.androidx.core.ktx)
    implementation(libs.androidx.activity.compose)
    implementation(platform(libs.androidx.compose.bom))
    implementation(libs.androidx.compose.ui)
    implementation(libs.androidx.compose.ui.tooling.preview)
    implementation(libs.androidx.compose.material3)

    // 后台定时刷新（前台 60s 由界面协程负责，后台受 WorkManager 最小 15 分钟限制）
    implementation("androidx.work:work-runtime-ktx:2.9.0")
    // 桌面小组件（Glance）
    implementation("androidx.glance:glance-appwidget:1.1.0")
    // ViewModel / 生命周期（Compose 集成）
    implementation("androidx.lifecycle:lifecycle-viewmodel-compose:2.8.7")
    implementation("androidx.lifecycle:lifecycle-runtime-compose:2.8.7")

    // outline 风格图标集（Icons.Outlined.*）
    implementation("androidx.compose.material:material-icons-extended")

    debugImplementation(libs.androidx.compose.ui.tooling)

    // 纯 JVM 单测：org.json 在 Android 上是系统实现，单测里用官方 JVM 版替代
    testImplementation("junit:junit:4.13.2")
    testImplementation("org.json:json:20240303")
}
