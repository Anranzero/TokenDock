namespace TokenDock;

/// <summary>
/// 应用展示状态：保存最近一次成功的数据。失败时保留旧数据并标记“过期”，绝不显示为满额。
/// </summary>
public sealed class AppState
{
    public UsageData? LastGood { get; private set; }
    public bool IsStale { get; private set; }

    /// <summary>当前状态说明（中文）。</summary>
    public string StatusText { get; private set; } = "尚未获取数据";

    public FetchFailureKind LastFailureKind { get; private set; } = FetchFailureKind.None;

    public void Apply(FetchResult result)
    {
        if (result.Success && result.Data is not null)
        {
            LastGood = result.Data;
            IsStale = false;
            LastFailureKind = FetchFailureKind.None;
            StatusText = $"✔ 已更新 · {result.Data.FetchedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}";
            return;
        }

        LastFailureKind = result.FailureKind;
        if (LastGood is null)
        {
            IsStale = false;
            StatusText = $"✖ 获取失败：{result.ErrorMessage}";
        }
        else
        {
            IsStale = true;
            StatusText = $"⚠ 数据已过期 · {result.ErrorMessage}"
                + $" · 最后成功：{LastGood.FetchedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}";
        }
    }
}
