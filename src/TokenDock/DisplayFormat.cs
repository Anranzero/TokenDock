using System.Globalization;

namespace TokenDock;

/// <summary>中文展示格式化。</summary>
public static class DisplayFormat
{
    /// <summary>剩余百分比文本；缺失时显示“未知”，不编造数据。</summary>
    public static string FormatRemainingPercent(double? remaining)
        => remaining is null ? "未知" : FormatRemainingNumber(remaining) + "%";

    /// <summary>剩余百分比的整数文本（不含 % 号）；缺失时显示“未知”，不编造数据。</summary>
    public static string FormatRemainingNumber(double? remaining)
        => remaining is null
            ? "未知"
            : Math.Round(remaining.Value, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture);

    /// <summary>把接口返回的 status 字段转成中文；无法识别时原样展示。</summary>
    public static string FormatStatus(string? status) => (status ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "" => "状态未知",
        "ok" or "normal" or "healthy" or "active" => "正常",
        "warn" or "warning" or "elevated" => "偏高",
        "limit" or "limited" or "exceeded" or "exhausted" or "blocked" or "throttled" => "已限流",
        _ => status!,
    };

    /// <summary>重置倒计时文本（自然中文，如「3小时44分钟后重置」；整点省略分钟）。</summary>
    public static string FormatCountdown(DateTimeOffset? resetsAtUtc, DateTimeOffset nowUtc)
    {
        if (resetsAtUtc is null) return "重置时间未知";
        var remain = resetsAtUtc.Value - nowUtc;
        if (remain <= TimeSpan.Zero) return "已到重置时间，等待刷新";
        if (remain.TotalDays >= 1)
            return remain.Hours == 0
                ? $"{(int)remain.TotalDays}天后重置"
                : $"{(int)remain.TotalDays}天{remain.Hours}小时后重置";
        if (remain.TotalHours >= 1)
            return remain.Minutes == 0
                ? $"{(int)remain.TotalHours}小时后重置"
                : $"{(int)remain.TotalHours}小时{remain.Minutes}分钟后重置";
        if (remain.TotalMinutes >= 1)
            return $"{(int)remain.TotalMinutes}分钟后重置";
        return "不足1分钟后重置";
    }
}
