namespace TokenDock;

/// <summary>单个用量窗口（滚动 / 本周 / 本月）的原始数据。</summary>
public sealed class UsageWindow
{
    /// <summary>已用百分比；接口未返回时为 NaN。</summary>
    public double PercentUsed { get; init; } = double.NaN;

    /// <summary>接口返回的 status 字段原样保留（例如 ok / warning / limited，真实取值待实测）。</summary>
    public string? Status { get; init; }

    /// <summary>重置时间；接口未返回时为 null。</summary>
    public DateTimeOffset? ResetsAt { get; init; }

    /// <summary>剩余百分比 = 100 - 已用百分比，钳制在 0~100；数据缺失时返回 null，不编造数据。</summary>
    public double? RemainingPercent
    {
        get
        {
            if (double.IsNaN(PercentUsed)) return null;
            return Math.Clamp(100.0 - PercentUsed, 0, 100);
        }
    }
}

/// <summary>一次成功拉取到的完整用量数据。</summary>
public sealed class UsageData
{
    public UsageWindow? Rolling { get; init; }
    public UsageWindow? Weekly { get; init; }
    public UsageWindow? Monthly { get; init; }
    public DateTimeOffset FetchedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>拉取失败的分类。</summary>
public enum FetchFailureKind
{
    None,
    /// <summary>尚未设置 API 密钥。</summary>
    NotConfigured,
    /// <summary>断网、DNS 失败、超时等网络问题。</summary>
    NetworkError,
    /// <summary>密钥无效或已失效（HTTP 401，已实测）。</summary>
    InvalidKey,
    /// <summary>未订阅或无权限（HTTP 402 / 403，具体码值待实测）。</summary>
    NoSubscription,
    /// <summary>请求过于频繁（HTTP 429）。</summary>
    RateLimited,
    /// <summary>服务端异常或其他非预期状态码。</summary>
    ServerError,
    /// <summary>HTTP 200 但响应内容不符合预期。</summary>
    ParseError,
}

/// <summary>单个模型在某个周期内的实际扣费（美元）。当前接口未提供该数据，结构保留以备接口上线。</summary>
public sealed class ModelUsageLine
{
    public string Model { get; init; } = "";
    public decimal CostUsd { get; init; }
}

/// <summary>模型级金额合计计算。</summary>
public static class ModelUsageMath
{
    public static decimal TotalUsd(IReadOnlyList<ModelUsageLine> lines)
    {
        decimal total = 0m;
        foreach (var line in lines)
            total += line.CostUsd;
        return total;
    }
}

/// <summary>一次拉取的结果：成功携带数据，失败携带分类与中文提示。</summary>
public sealed class FetchResult
{
    public bool Success { get; private init; }
    public UsageData? Data { get; private init; }
    public FetchFailureKind FailureKind { get; private init; }

    /// <summary>面向用户的中文错误说明。</summary>
    public string? ErrorMessage { get; private init; }

    /// <summary>服务端返回的原始 message（可能为空），仅用于排查；内容来自服务端，不含本机密钥。</summary>
    public string? ServerDetail { get; private init; }

    public static FetchResult Ok(UsageData data) => new() { Success = true, Data = data };

    public static FetchResult Fail(FetchFailureKind kind, string message, string? detail = null)
        => new() { Success = false, FailureKind = kind, ErrorMessage = message, ServerDetail = detail };
}
