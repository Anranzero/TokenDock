package com.tokendock.app.data

import android.content.Context
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import android.util.Base64
import java.security.KeyStore
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec

/**
 * API Key 安全存储：密钥由 Android Keystore 生成并保管（AES-256/GCM，不可导出），
 * 密文（IV + ciphertext，Base64）存放在应用私有 SharedPreferences。
 *
 * 约束（与需求一致）：
 * - 明文绝不写入文件/日志/第三方；
 * - 本类不提供任何回显接口，仅 [load] 返回明文供请求 Authorization 头使用；
 * - 卸载应用或清除数据即失效（Keystore 条目随应用删除）。
 */
object SecureKeyStore {

    private const val PREFS_NAME = "tokendock_secure"
    private const val PREF_CIPHERTEXT = "api_key_ciphertext"
    private const val KEY_ALIAS = "tokendock_api_key_v1"
    private const val ANDROID_KEYSTORE = "AndroidKeyStore"
    private const val TRANSFORMATION = "AES/GCM/NoPadding"
    private const val GCM_TAG_BITS = 128

    fun save(context: Context, apiKey: String) {
        val cipher = Cipher.getInstance(TRANSFORMATION)
        cipher.init(Cipher.ENCRYPT_MODE, secretKey())
        val ciphertext = cipher.doFinal(apiKey.toByteArray(Charsets.UTF_8))
        // IV 与密文一起存（IV 非敏感，GCM 要求每次加密随机生成）
        val payload = ByteArray(cipher.iv.size + ciphertext.size)
        System.arraycopy(cipher.iv, 0, payload, 0, cipher.iv.size)
        System.arraycopy(ciphertext, 0, payload, cipher.iv.size, ciphertext.size)
        prefs(context).edit()
            .putString(PREF_CIPHERTEXT, Base64.encodeToString(payload, Base64.NO_WRAP))
            .apply()
    }

    /** 读取明文密钥；未保存 / 解密失败（换机、Keystore 失效）时返回 null。 */
    fun load(context: Context): String? {
        val encoded = prefs(context).getString(PREF_CIPHERTEXT, null) ?: return null
        return try {
            val payload = Base64.decode(encoded, Base64.NO_WRAP)
            val ivSize = 12
            if (payload.size <= ivSize) return null
            val iv = payload.copyOfRange(0, ivSize)
            val ciphertext = payload.copyOfRange(ivSize, payload.size)
            val cipher = Cipher.getInstance(TRANSFORMATION)
            cipher.init(Cipher.DECRYPT_MODE, secretKey(), GCMParameterSpec(GCM_TAG_BITS, iv))
            cipher.doFinal(ciphertext).toString(Charsets.UTF_8)
        } catch (e: Exception) {
            null
        }
    }

    fun hasKey(context: Context): Boolean = prefs(context).contains(PREF_CIPHERTEXT)

    fun clear(context: Context) {
        prefs(context).edit().remove(PREF_CIPHERTEXT).apply()
    }

    private fun prefs(context: Context) =
        context.applicationContext.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)

    private fun secretKey(): SecretKey {
        val keyStore = KeyStore.getInstance(ANDROID_KEYSTORE).apply { load(null) }
        (keyStore.getEntry(KEY_ALIAS, null) as? KeyStore.SecretKeyEntry)?.let { return it.secretKey }

        val generator = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, ANDROID_KEYSTORE)
        generator.init(
            KeyGenParameterSpec.Builder(
                KEY_ALIAS,
                KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT,
            )
                .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
                .setKeySize(256)
                .setUserAuthenticationRequired(false)
                .build(),
        )
        return generator.generateKey()
    }
}
