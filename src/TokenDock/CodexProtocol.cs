using System.Text.Json;

namespace TokenDock;

/// <summary>Codex app-server 协议异常（含分类，便于 UI 与测试）。</summary>
public sealed class CodexException : Exception
{
    public CodexFailureKind Kind { get; }

    public CodexException(CodexFailureKind kind, string message, Exception? inner = null) : base(message, inner)
        => Kind = kind;
}

public enum CodexFailureKind
{
    NotInstalled,
    StartFailed,
    Timeout,
    ProcessExited,
    RpcError,
    Protocol,
    LoginFailed,
}

/// <summary>
/// Codex app-server 纯协议层：JSONL 帧构造与容错解析（不涉及进程，可单元测试）。
/// 线上格式为 JSON-RPC 2.0 去掉 jsonrpc 头、一行一个 JSON（JSONL）。
/// 字段命名以 app-server v2（camelCase）为准，同时兼容上游 snake_case 作为兜底。
/// </summary>
public static class CodexProtocol
{
    public const string MethodInitialize = "initialize";
    public const string MethodInitialized = "initialized";
    public const string MethodAccountRead = "account/read";
    public const string MethodLoginStart = "account/login/start";
    public const string MethodRateLimitsRead = "account/rateLimits/read";
    public const string MethodRateLimitsUpdated = "account/rateLimits/updated";

    public static string BuildInitialize(int id, string clientName, string version)
        => $"{{\"method\":\"{MethodInitialize}\",\"id\":{id},\"params\":{{\"clientInfo\":{{\"name\":{Json(clientName)},\"title\":\"AI 用量助手（TokenDock）\",\"version\":{Json(version)}}}}}}}";

    public static string BuildInitialized()
        => $"{{\"method\":\"{MethodInitialized}\",\"params\":{{}}}}";

    public static string BuildAccountRead(int id)
        => $"{{\"method\":\"{MethodAccountRead}\",\"id\":{id},\"params\":{{}}}}";

    public static string BuildLoginStart(int id)
        => $"{{\"method\":\"{MethodLoginStart}\",\"id\":{id},\"params\":{{\"type\":\"chatgpt\"}}}}";

    public static string BuildRateLimitsRead(int id)
        => $"{{\"method\":\"{MethodRateLimitsRead}\",\"id\":{id},\"params\":{{}}}}";

    private static string Json(string value) => JsonSerializer.Serialize(value);

    /// <summary>解析 account/read 结果：account 为 null 即未登录（v2 GetAccountResponse）。</summary>
    public static CodexAccountInfo ParseAccount(JsonElement result)
    {
        var requiresAuth = TryBool(result, "requiresOpenaiAuth") ?? TryBool(result, "requires_openai_auth") ?? true;
        if (!result.TryGetProperty("account", out var account) || account.ValueKind != JsonValueKind.Object)
            return new CodexAccountInfo { LoggedIn = false, RequiresOpenAiAuth = requiresAuth };

        return new CodexAccountInfo
        {
            LoggedIn = true,
            AccountType = TryString(account, "type"),
            Email = TryString(account, "email"),
            PlanType = TryString(account, "planType") ?? TryString(account, "plan_type"),
            RequiresOpenAiAuth = requiresAuth,
        };
    }

    /// <summary>解析 account/login/start 结果（chatgpt 变体：loginId + authUrl）。</summary>
    public static (string? LoginId, string? AuthUrl, string? Type) ParseLoginStart(JsonElement result)
    {
        return (
            TryString(result, "loginId") ?? TryString(result, "login_id"),
            TryString(result, "authUrl") ?? TryString(result, "auth_url") ?? TryString(result, "verificationUrl") ?? TryString(result, "verification_url"),
            TryString(result, "type"));
    }

    /// <summary>
    /// 解析 account/rateLimits/read 结果（v2 GetAccountRateLimitsResponse）：
    /// rateLimits 主分组 + rateLimitsByLimitId 附加分组；官方返回中 byLimitId 会重复引用主分组，
    /// 因此按 limitId 去重（真实数据实测发现）。无任何窗口时抛出 Protocol 异常。
    /// </summary>
    public static CodexRateLimitsSnapshot ParseRateLimits(JsonElement result)
    {
        var groups = new List<CodexRateGroup>();
        var seenLimitIds = new HashSet<string>(StringComparer.Ordinal);

        var maiElement = result.TryGetProperty("rateLimits", out var main) && main.ValueKind == JsonValueKind.Object
            ? main
            : result.TryGetProperty("rate_limits", out var mainSnake) && mainSnake.ValueKind == JsonValueKind.Object
                ? mainSnake
                : default;

        if (maiElement.ValueKind == JsonValueKind.Object)
        {
            var group = BuildGroup(maiElement);
            seenLimitIds.Add(group.LimitId ?? "codex");
            groups.Add(group);
        }

        if (result.TryGetProperty("rateLimitsByLimitId", out var byId) && byId.ValueKind == JsonValueKind.Object
            || result.TryGetProperty("rate_limits_by_limit_id", out byId) && byId.ValueKind == JsonValueKind.Object)
        {
            foreach (var entry in byId.EnumerateObject())
            {
                if (entry.Value.ValueKind != JsonValueKind.Object) continue;
                var group = BuildGroup(entry.Value, entry.Name);
                if (!seenLimitIds.Add(group.LimitId ?? entry.Name))
                    continue; // 与主分组同一额度（重复引用），跳过
                groups.Add(group);
            }
        }

        groups.RemoveAll(g => !g.HasAnyWindow);
        if (groups.Count == 0)
        {
            throw new CodexException(CodexFailureKind.Protocol,
                "响应中没有可用的额度窗口（字段可能为空或尚未提供该额度）。");
        }

        return new CodexRateLimitsSnapshot
        {
            Groups = groups,
            AccountId = TryString(result, "accountId") ?? TryString(result, "account_id"),
        };
    }

    private static CodexRateGroup BuildGroup(JsonElement snapshot, string? fallbackLimitId = null)
    {
        return new CodexRateGroup
        {
            LimitId = TryString(snapshot, "limitId") ?? TryString(snapshot, "limit_id") ?? fallbackLimitId,
            LimitName = TryString(snapshot, "limitName") ?? TryString(snapshot, "limit_name"),
            Primary = ParseWindow(snapshot, "primary"),
            Secondary = ParseWindow(snapshot, "secondary"),
        };
    }

    /// <summary>解析单个 RateLimitWindow：usedPercent 必填，windowDurationMins / resetsAt 可为 null。</summary>
    public static CodexRateWindow? ParseWindow(JsonElement snapshot, string propertyName)
    {
        if (!snapshot.TryGetProperty(propertyName, out var window) || window.ValueKind != JsonValueKind.Object)
            return null;

        var used = TryDouble(window, "usedPercent") ?? TryDouble(window, "used_percent");
        if (used is null) return null;

        return new CodexRateWindow
        {
            UsedPercent = used.Value,
            WindowDurationMins = TryLong(window, "windowDurationMins") ?? TryLong(window, "window_duration_mins"),
            ResetsAtUnixSeconds = TryLong(window, "resetsAt") ?? TryLong(window, "resets_at"),
        };
    }

    /// <summary>从 JSON-RPC error 对象生成中文描述（服务端 message 原文保留）。</summary>
    public static string DescribeError(JsonElement error)
    {
        var code = TryLong(error, "code");
        var message = TryString(error, "message") ?? "未知错误";
        return code is null ? message : $"{message}（代码 {code}）";
    }

    private static string? TryString(JsonElement obj, string name)
        => obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static double? TryDouble(JsonElement obj, string name)
        => obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var d)
            ? d
            : null;

    private static long? TryLong(JsonElement obj, string name)
        => obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var l)
            ? l
            : null;

    private static bool? TryBool(JsonElement obj, string name)
        => obj.TryGetProperty(name, out var value) && (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False)
            ? value.GetBoolean()
            : null;
}
