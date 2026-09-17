using System.Security.Cryptography;
using System.Text;

namespace TokenDock;

/// <summary>
/// API 密钥本机加密存储：使用 Windows DPAPI（当前用户作用域）加密后保存到
/// %APPDATA%\TokenDock\apikey.bin。密钥不会写入日志、不会随源码提交、
/// 也不会发送到 OpenCode / Codex 接口以外的任何第三方。
/// </summary>
public static class SecureKeyStore
{
    // 注意：该 entropy 必须保持历史值（改名 TokenDock 前保存的密钥按此加密），
    // 修改会导致旧密钥无法解密、用户必须重新输入。
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("OpenCodeGoAssistant.LocalKey.v1");

    public static string StoreDirectory => AppDataPaths.Directory;

    private static string StoreFilePath => Path.Combine(StoreDirectory, "apikey.bin");

    public static bool HasKey
    {
        get
        {
            AppDataPaths.EnsureMigrated();
            return File.Exists(StoreFilePath);
        }
    }

    /// <exception cref="System.IO.IOException">写入失败时抛出，由调用方提示用户。</exception>
    public static void Save(string apiKey)
    {
        AppDataPaths.EnsureMigrated();
        Directory.CreateDirectory(StoreDirectory);
        var encrypted = ProtectedData.Protect(Encoding.UTF8.GetBytes(apiKey), Entropy, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(StoreFilePath, encrypted);
    }

    /// <summary>读取密钥；不存在、解密失败或文件读取异常（被占用 / 无权限）时返回 null，由用户重新设置。</summary>
    public static string? Load()
    {
        try
        {
            AppDataPaths.EnsureMigrated();
            if (!File.Exists(StoreFilePath)) return null;
            var plain = ProtectedData.Unprotect(File.ReadAllBytes(StoreFilePath), Entropy, DataProtectionScope.CurrentUser);
            var key = Encoding.UTF8.GetString(plain);
            return string.IsNullOrWhiteSpace(key) ? null : key;
        }
        catch (CryptographicException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    // ---- GLM Coding Plan 密钥（Z.ai / BigModel 各自独立文件，账号与数据不混用） ----

    private static string GlmFilePath(GlmProvider provider)
        => Path.Combine(StoreDirectory, provider == GlmProvider.Zai ? "glm-apikey-zai.bin" : "glm-apikey-bigmodel.bin");

    public static bool HasGlmKey(GlmProvider provider)
    {
        AppDataPaths.EnsureMigrated();
        return File.Exists(GlmFilePath(provider));
    }

    /// <exception cref="System.IO.IOException">写入失败时抛出，由调用方提示用户。</exception>
    public static void SaveGlm(GlmProvider provider, string apiKey)
    {
        AppDataPaths.EnsureMigrated();
        Directory.CreateDirectory(StoreDirectory);
        var encrypted = ProtectedData.Protect(Encoding.UTF8.GetBytes(apiKey), Entropy, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(GlmFilePath(provider), encrypted);
    }

    public static string? LoadGlm(GlmProvider provider)
    {
        try
        {
            AppDataPaths.EnsureMigrated();
            var path = GlmFilePath(provider);
            if (!File.Exists(path)) return null;
            var plain = ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser);
            var key = Encoding.UTF8.GetString(plain);
            return string.IsNullOrWhiteSpace(key) ? null : key;
        }
        catch (CryptographicException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
