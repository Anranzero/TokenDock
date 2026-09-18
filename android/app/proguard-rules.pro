# 默认无需额外规则：未启用混淆（isMinifyEnabled = false）。
# 若开启混淆，请保留 Keystore 相关实现与 Glance/WorkManager 的反射入口：
-keep class com.tokendock.app.data.SecureKeyStore { *; }
-keep class androidx.glance.** { *; }
-keep class androidx.work.** { *; }
