using System.Text.Json;
using Xunit;

namespace TokenDock.Tests;

/// <summary>Codex 协议层测试：帧构造、容错解析（camelCase/snake_case、空字段、多分组）、窗口动态命名。</summary>
public class CodexProtocolTests
{
    [Fact]
    public void Builders_ProduceJsonRpcShape()
    {
        Assert.Contains("\"method\":\"initialize\"", CodexProtocol.BuildInitialize(7, "client", "1.0"));
        Assert.Contains("\"id\":7", CodexProtocol.BuildInitialize(7, "client", "1.0"));
        Assert.Contains("\"clientInfo\"", CodexProtocol.BuildInitialize(7, "client", "1.0"));
        Assert.Equal("{\"method\":\"initialized\",\"params\":{}}", CodexProtocol.BuildInitialized());
        Assert.Contains("account/rateLimits/read", CodexProtocol.BuildRateLimitsRead(9));
        Assert.Contains("\"type\":\"chatgpt\"", CodexProtocol.BuildLoginStart(3));
    }

    [Fact]
    public void ParseAccount_LoggedIn()
    {
        using var doc = JsonDocument.Parse(
            "{\"account\":{\"type\":\"chatgpt\",\"email\":\"a@b.com\",\"planType\":\"plus\"},\"requiresOpenaiAuth\":true}");
        var info = CodexProtocol.ParseAccount(doc.RootElement);

        Assert.True(info.LoggedIn);
        Assert.Equal("a@b.com", info.Email);
        Assert.Equal("plus", info.PlanType);
        Assert.Contains("a@b.com", info.DisplayLine);
    }

    [Fact]
    public void ParseAccount_LoggedOut()
    {
        using var doc = JsonDocument.Parse("{\"account\":null,\"requiresOpenaiAuth\":true}");
        Assert.False(CodexProtocol.ParseAccount(doc.RootElement).LoggedIn);
    }

    [Fact]
    public void ParseLoginStart_ChatgptVariant()
    {
        using var doc = JsonDocument.Parse(
            "{\"type\":\"chatgpt\",\"loginId\":\"c1f96a13\",\"authUrl\":\"https://auth.openai.com/oauth/authorize?x=1\"}");
        var (loginId, authUrl, type) = CodexProtocol.ParseLoginStart(doc.RootElement);

        Assert.Equal("c1f96a13", loginId);
        Assert.StartsWith("https://auth.openai.com/", authUrl);
        Assert.Equal("chatgpt", type);
    }

    [Fact]
    public void ParseRateLimits_RealShape_DedupesByLimitIdDuplicateOfMainGroup()
    {
        // 实机 codex-cli 0.154.0 真实返回：rateLimitsByLimitId 同时包含主分组 codex 的重复引用与附加分组，
        // 早期实现会渲染出重复的“7天窗口”卡片（已修复）
        using var doc = JsonDocument.Parse("""
        {
          "ordinaryUsageAllowed": true,
          "rateLimits": { "limitId": "codex", "limitName": null,
            "primary": { "usedPercent": 59, "windowDurationMins": 10080, "resetsAt": 1789807013 }, "secondary": null },
          "rateLimitsByLimitId": {
            "codex_bengalfox": { "limitId": "codex_bengalfox", "limitName": "GPT-5.3-Codex-Spark",
              "primary": { "usedPercent": 0, "windowDurationMins": 300, "resetsAt": 1789653901 },
              "secondary": { "usedPercent": 0, "windowDurationMins": 10080, "resetsAt": 1790240701 } },
            "codex": { "limitId": "codex", "limitName": null,
              "primary": { "usedPercent": 59, "windowDurationMins": 10080, "resetsAt": 1789807013 }, "secondary": null }
          }
        }
        """);
        var snapshot = CodexProtocol.ParseRateLimits(doc.RootElement);
        var levels = CodexLevelView.Flatten(snapshot);

        Assert.Equal(2, snapshot.Groups.Count);
        Assert.Equal(3, levels.Count); // 7天窗口(主) + Spark 5小时 + Spark 7天，不再重复
        Assert.Equal(41, levels[0].Window.RemainingPercent);
        Assert.Equal("7天窗口", levels[0].Title);
        Assert.Equal("GPT-5.3-Codex-Spark · 5小时窗口", levels[1].Title);
    }

    [Fact]
    public void ParseRateLimits_CamelCase_MainAndAdditionalGroups()
    {
        using var doc = JsonDocument.Parse("""
        {
          "rateLimits": {
            "limitId": "codex",
            "primary": { "usedPercent": 57, "windowDurationMins": 10080, "resetsAt": 1789807013 },
            "secondary": null
          },
          "rateLimitsByLimitId": {
            "codex_bengalfox": {
              "limitId": "codex_bengalfox",
              "limitName": "GPT-5.3-Codex-Spark",
              "primary": { "usedPercent": 0, "windowDurationMins": 300, "resetsAt": 1789649361 },
              "secondary": { "usedPercent": 12, "windowDurationMins": 10080, "resetsAt": null }
            }
          }
        }
        """);
        var snapshot = CodexProtocol.ParseRateLimits(doc.RootElement);
        var levels = CodexLevelView.Flatten(snapshot);

        Assert.Equal(2, snapshot.Groups.Count);
        Assert.Equal(3, levels.Count);
        Assert.Equal(43, levels[0].Window.RemainingPercent);
        Assert.Equal("7天窗口", levels[0].Title);
        Assert.Equal("GPT-5.3-Codex-Spark · 5小时窗口", levels[1].Title);
        Assert.Equal("重置时间未提供", levels[2].Window.CountdownText);
        Assert.Equal(88, levels[2].Window.RemainingPercent);
    }

    [Fact]
    public void ParseRateLimits_SnakeCaseFallback()
    {
        using var doc = JsonDocument.Parse("""
        { "rate_limits": { "primary": { "used_percent": 10, "window_duration_mins": 300, "resets_at": 1789649361 } } }
        """);
        var snapshot = CodexProtocol.ParseRateLimits(doc.RootElement);

        Assert.Single(snapshot.Groups);
        Assert.Equal(90, snapshot.Groups[0].Primary!.RemainingPercent);
        Assert.Equal(1789649361, snapshot.Groups[0].Primary!.ResetsAtUnixSeconds);
    }

    [Fact]
    public void ParseRateLimits_EmptyWindows_ThrowsProtocol()
    {
        using var doc = JsonDocument.Parse("{\"rateLimits\":{\"primary\":null,\"secondary\":null}}");
        var ex = Assert.Throws<CodexException>(() => CodexProtocol.ParseRateLimits(doc.RootElement));
        Assert.Equal(CodexFailureKind.Protocol, ex.Kind);
    }

    [Fact]
    public void ParseWindow_MissingUsedPercent_ReturnsNull()
    {
        using var doc = JsonDocument.Parse("{\"primary\":{\"windowDurationMins\":300}}");
        Assert.Null(CodexProtocol.ParseWindow(doc.RootElement, "primary"));
    }

    [Theory]
    [InlineData(300L, "5小时窗口")]
    [InlineData(1080L, "18小时窗口")]
    [InlineData(10080L, "7天窗口")]
    [InlineData(90L, "1小时30分钟窗口")]
    [InlineData(45L, "45分钟窗口")]
    [InlineData(null, "额度窗口")]
    public void WindowName_IsDerivedFromDuration(long? mins, string expected)
        => Assert.Equal(expected, CodexFormat.WindowName(mins));

    [Fact]
    public void DescribeError_KeepsServerMessage()
    {
        using var doc = JsonDocument.Parse("{\"code\":-32603,\"message\":\"failed to fetch codex rate limits\"}");
        var text = CodexProtocol.DescribeError(doc.RootElement);
        Assert.Contains("failed to fetch codex rate limits", text);
        Assert.Contains("-32603", text);
    }
}

/// <summary>Codex 状态迁移测试：失败保留旧数据并标记过期、登录失效、未安装。</summary>
public class CodexStatePolicyTests
{
    private static CodexRateLimitsSnapshot SampleSnapshot() => new()
    {
        Groups = new[]
        {
            new CodexRateGroup
            {
                Primary = new CodexRateWindow { UsedPercent = 57, WindowDurationMins = 10080, ResetsAtUnixSeconds = 1789807013 },
            },
        },
    };

    [Fact]
    public void ApplySnapshot_SetsReadyAndClearsStale()
    {
        var state = new CodexState();
        CodexStatePolicy.ApplySnapshot(state, new CodexAccountInfo { LoggedIn = true, Email = "a@b.com" }, SampleSnapshot());

        Assert.Equal(CodexAvailability.Ready, state.Availability);
        Assert.False(state.IsStale);
        Assert.NotNull(state.LastGood);
        Assert.Contains("正常", state.StatusText);
    }

    [Fact]
    public void ApplyFailure_KeepsOldDataAndMarksStale()
    {
        var state = new CodexState();
        CodexStatePolicy.ApplySnapshot(state, new CodexAccountInfo { LoggedIn = true }, SampleSnapshot());
        CodexStatePolicy.ApplyFailure(state, "Codex 进程已退出");

        Assert.Equal(CodexAvailability.Failed, state.Availability);
        Assert.True(state.IsStale);
        Assert.NotNull(state.LastGood);
        Assert.Equal(43, state.LastGood!.Groups[0].Primary!.RemainingPercent); // 旧数据保留，不回退为满额
        Assert.Contains("最后成功", state.StatusText);
    }

    [Fact]
    public void ApplyFailure_WithoutPriorData_ShowsReasonOnly()
    {
        var state = new CodexState();
        CodexStatePolicy.ApplyFailure(state, "网络连接失败");

        Assert.Null(state.LastGood);
        Assert.False(state.IsStale);
        Assert.Contains("网络连接失败", state.StatusText);
    }

    [Fact]
    public void ApplyNotLoggedIn_WithOldData_MarksLoginExpired()
    {
        var state = new CodexState();
        CodexStatePolicy.ApplySnapshot(state, new CodexAccountInfo { LoggedIn = true }, SampleSnapshot());
        CodexStatePolicy.ApplyNotLoggedIn(state, new CodexAccountInfo { LoggedIn = false });

        Assert.Equal(CodexAvailability.NotLoggedIn, state.Availability);
        Assert.True(state.IsStale);
        Assert.Contains("登录已失效", state.StatusText);
    }

    [Fact]
    public void ApplyNotInstalled_SetsAvailability()
    {
        var state = new CodexState();
        CodexStatePolicy.ApplyNotInstalled(state);

        Assert.Equal(CodexAvailability.NotInstalled, state.Availability);
        Assert.Contains("Codex CLI", state.StatusText);
    }
}
