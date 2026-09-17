namespace TokenDock;

/// <summary>Codex 单个额度窗口（app-server v2 RateLimitWindow，camelCase 线上格式）。</summary>
public sealed class CodexRateWindow
{
    /// <summary>已用百分比（0-100）。</summary>
    public double UsedPercent { get; init; }

    /// <summary>窗口时长（分钟）；字段缺失为 null，命名需按实际值动态生成。</summary>
    public long? WindowDurationMins { get; init; }

    /// <summary>重置时间（Unix 秒）；字段缺失为 null。</summary>
    public long? ResetsAtUnixSeconds { get; init; }

    /// <summary>剩余百分比 = 100 - usedPercent，钳制 0~100。</summary>
    public double RemainingPercent => Math.Clamp(100.0 - UsedPercent, 0, 100);

    /// <summary>剩余百分比文本（usedPercent 为接口必填字段）。</summary>
    public string RemainingText => DisplayFormat.FormatRemainingPercent(RemainingPercent);

    /// <summary>重置倒计时文本；resetsAt 缺失时明确显示“未提供”。</summary>
    public string CountdownText => ResetsAtUnixSeconds is { } seconds
        ? DisplayFormat.FormatCountdown(DateTimeOffset.FromUnixTimeSeconds(seconds), DateTimeOffset.UtcNow)
        : "重置时间未提供";
}

/// <summary>一个额度分组（主分组 rateLimits 或 rateLimitsByLimitId 中的附加分组）。</summary>
public sealed class CodexRateGroup
{
    public string? LimitId { get; init; }
    public string? LimitName { get; init; }
    public CodexRateWindow? Primary { get; init; }
    public CodexRateWindow? Secondary { get; init; }

    public bool HasAnyWindow => Primary is not null || Secondary is not null;
}

/// <summary>一次成功的 Codex 额度快照。</summary>
public sealed class CodexRateLimitsSnapshot
{
    public IReadOnlyList<CodexRateGroup> Groups { get; init; } = Array.Empty<CodexRateGroup>();
    public DateTimeOffset FetchedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public string? AccountId { get; init; }
}

/// <summary>Codex 账户信息（account/read 结果）。</summary>
public sealed class CodexAccountInfo
{
    public bool LoggedIn { get; init; }
    public string? Email { get; init; }
    public string? PlanType { get; init; }
    public string? AccountType { get; init; }
    public bool RequiresOpenAiAuth { get; init; }

    public string DisplayLine => LoggedIn
        ? (string.IsNullOrWhiteSpace(Email) ? "已登录 ChatGPT" : $"已登录 · {Email}") + (PlanType is null ? "" : $"（{PlanType}）")
        : "未登录 ChatGPT";
}

/// <summary>Codex 页可用状态。</summary>
public enum CodexAvailability
{
    Unknown,
    /// <summary>未检测到本地 Codex CLI。</summary>
    NotInstalled,
    /// <summary>已检测到但未登录。</summary>
    NotLoggedIn,
    Ready,
    /// <summary>获取失败（登录失效 / 断网 / 进程退出 / 接口错误），保留旧数据标过期。</summary>
    Failed,
}

/// <summary>Codex 页面状态（失败保留旧数据并标记过期，不回退为满额）。</summary>
public sealed class CodexState
{
    public CodexAvailability Availability { get; set; } = CodexAvailability.Unknown;
    public CodexRateLimitsSnapshot? LastGood { get; set; }
    public CodexAccountInfo? Account { get; set; }
    public bool IsStale { get; set; }
    public string StatusText { get; set; } = "尚未获取 Codex 余量";
    public bool LoginInProgress { get; set; }
}

/// <summary>Codex 状态迁移（纯函数，便于测试失败路径）。</summary>
public static class CodexStatePolicy
{
    public static void ApplySnapshot(CodexState state, CodexAccountInfo account, CodexRateLimitsSnapshot snapshot)
    {
        state.Account = account;
        state.LastGood = snapshot;
        state.IsStale = false;
        state.Availability = CodexAvailability.Ready;
        state.LoginInProgress = false;
        state.StatusText = $"正常 · 更新于 {snapshot.FetchedAtUtc.ToLocalTime():HH:mm}";
    }

    public static void ApplyNotLoggedIn(CodexState state, CodexAccountInfo account)
    {
        state.Account = account;
        state.Availability = CodexAvailability.NotLoggedIn;
        if (state.LastGood is not null)
        {
            state.IsStale = true;
            state.StatusText = "登录已失效 · 显示上次数据";
        }
        else
        {
            state.StatusText = "未登录 ChatGPT";
        }
    }

    public static void ApplyNotInstalled(CodexState state)
    {
        state.Availability = CodexAvailability.NotInstalled;
        state.IsStale = state.LastGood is not null;
        state.StatusText = "未检测到 Codex CLI";
    }

    /// <summary>失败：保留旧数据并标记过期；从未成功过时只显示失败原因。</summary>
    public static void ApplyFailure(CodexState state, string reason)
    {
        state.Availability = CodexAvailability.Failed;
        if (state.LastGood is not null)
        {
            state.IsStale = true;
            state.StatusText = $"数据已过期 · {reason} · 最后成功：{state.LastGood.FetchedAtUtc.ToLocalTime():HH:mm}";
        }
        else
        {
            state.IsStale = false;
            state.StatusText = $"获取失败：{reason}";
        }
    }
}

/// <summary>Codex 窗口命名与层级展开（按 windowDurationMins 动态命名，不套用固定“5小时/周/月”）。</summary>
public static class CodexFormat
{
    /// <summary>按窗口时长动态命名：300→「5小时窗口」、10080→「7天窗口」、90→「1小时30分钟窗口」。</summary>
    public static string WindowName(long? durationMins)
    {
        if (durationMins is not { } m || m <= 0) return "额度窗口";
        if (m < 60) return $"{m}分钟窗口";
        if (m % 1440 == 0) return $"{m / 1440}天窗口";
        if (m % 60 == 0) return $"{m / 60}小时窗口";
        return $"{m / 60}小时{m % 60}分钟窗口";
    }
}

/// <summary>页面展示的一条额度（分组 × 窗口展开后的一行）。</summary>
public sealed class CodexLevelView
{
    public string Title { get; init; } = "";
    public string GroupNote { get; init; } = "";
    public CodexRateWindow Window { get; init; } = null!;

    /// <summary>把快照按 分组×窗口 展开为展示行；空窗口自动跳过。</summary>
    public static IReadOnlyList<CodexLevelView> Flatten(CodexRateLimitsSnapshot snapshot)
    {
        var list = new List<CodexLevelView>();
        foreach (var group in snapshot.Groups)
        {
            AddLevel(list, group, group.Primary, "主窗口");
            AddLevel(list, group, group.Secondary, "副窗口");
        }
        return list;
    }

    private static void AddLevel(List<CodexLevelView> list, CodexRateGroup group, CodexRateWindow? window, string role)
    {
        if (window is null) return;
        var windowName = CodexFormat.WindowName(window.WindowDurationMins);
        var title = string.IsNullOrWhiteSpace(group.LimitName)
            ? windowName
            : $"{group.LimitName} · {windowName}";
        list.Add(new CodexLevelView
        {
            Title = title,
            GroupNote = string.IsNullOrWhiteSpace(group.LimitName) ? "" : role,
            Window = window,
        });
    }
}
