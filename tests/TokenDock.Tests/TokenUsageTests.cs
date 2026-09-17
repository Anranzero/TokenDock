using Xunit;

namespace TokenDock.Tests;

/// <summary>Token 采集的纯计算层测试：去重、窗口切片、聚合口径、格式化与官方 stats 文本解析。</summary>
public class TokenUsageTests
{
    private static TokenUsageRecord Record(
        string id, string model, double hoursAgo,
        long input = 0, long output = 0, long reasoning = 0, long cacheRead = 0, long cacheWrite = 0)
        => new()
        {
            MessageId = id,
            Model = model,
            CreatedUtc = DateTimeOffset.UtcNow.AddHours(-hoursAgo),
            Input = input,
            Output = output,
            Reasoning = reasoning,
            CacheRead = cacheRead,
            CacheWrite = cacheWrite,
            Total = input + output + reasoning + cacheRead + cacheWrite,
        };

    [Fact]
    public void Dedupe_ByMessageId_KeepsFirst()
    {
        var records = new[]
        {
            Record("msg_1", "a", 1, input: 10),
            Record("msg_1", "a", 1, input: 10),
            Record("msg_2", "a", 2, input: 20),
        };
        var deduped = TokenUsageMath.Dedupe(records);
        Assert.Equal(2, deduped.Count);
    }

    [Fact]
    public void Aggregate_OutputIncludesReasoning_AndOrdersByTotal()
    {
        var records = new[]
        {
            Record("m1", "small", 1, input: 100, output: 10, reasoning: 5, cacheRead: 1000),
            Record("m2", "big", 1, input: 200, output: 20, reasoning: 30, cacheRead: 5000, cacheWrite: 50),
            Record("m3", "big", 2, input: 300, output: 40, reasoning: 0, cacheRead: 1000),
        };
        var totals = TokenUsageMath.Aggregate(records);

        Assert.Equal("big", totals[0].Model);
        Assert.Equal(2, totals[0].Messages);
        Assert.Equal(500, totals[0].Input);
        Assert.Equal(90, totals[0].Output);      // 20+30 与 40+0：输出含推理，与官方 stats 口径一致
        Assert.Equal(6050, totals[0].Cache);     // 读 6000 + 写 50
        Assert.Equal("small", totals[1].Model);
        Assert.Equal(15, totals[1].Output);
    }

    [Theory]
    [InlineData(568L, "568")]
    [InlineData(913_518L, "913.5K")]
    [InlineData(2_145_431L, "2.1M")]
    [InlineData(133_933_032L, "133.9M")]
    [InlineData(1_200_000_000L, "1.2B")]
    public void FormatTokens_Compact(long value, string expected)
        => Assert.Equal(expected, TokenUsageMath.FormatTokens(value));

    [Theory]
    [InlineData("2.1M", 2_100_000)]
    [InlineData("913.5K", 913_500)]
    [InlineData("982", 982)]
    [InlineData("1.1B", 1_100_000_000)]
    [InlineData("0", 0)]
    [InlineData("", 0)]
    public void ParseValue_CliFormats(string text, double expected)
        => Assert.Equal(expected, OpenCodeStatsParser.ParseValue(text));

    [Fact]
    public void ParseGoModels_NonGoBlockDoesNotBleedIntoPreviousGoModel()
    {
        // 回归测试：非 Go 模型的区块出现在 Go 模型之后时，数字不得串到 Go 模型上
        const string output = """
│ opencode-go/glm-5.2                                    │
│  Messages                                          763 │
│  Input Tokens                                    14.6M │
│  Output Tokens                                  345.3K │
│  Cache Read                                      74.9M │
│  Cache Write                                         0 │
│  Cost                                         $41.4002 │
│ opencode/mimo-v2.5-free                                │
│  Messages                                          278 │
│  Input Tokens                                    831.7K │
│  Output Tokens                                  122.9K │
│  Cache Read                                       10.0M │
│  Cache Write                                         0 │
│  Cost                                          $0.0000 │
│ opencode-go/kimi-k3                                    │
│  Messages                                          145 │
│  Input Tokens                                   380.9K │
│  Output Tokens                                  124.0K │
│  Cache Read                                      11.8M │
│  Cache Write                                         0 │
""";
        var parsed = OpenCodeStatsParser.ParseGoModels(output);

        Assert.Equal(2, parsed.Count);
        Assert.Equal(763, parsed["glm-5.2"].Messages);
        Assert.Equal(14_600_000, parsed["glm-5.2"].Input, 1.0);
        Assert.Equal(74_900_000, parsed["glm-5.2"].CacheRead, 1.0);
        Assert.Equal(145, parsed["kimi-k3"].Messages);
    }

    [Fact]
    public void ParseGoModels_FromRealCliSnippet()
    {
        const string output = """
│ opencode-go/deepseek-v4-flash                          │
│  Messages                                          982 │
│  Input Tokens                                     2.1M │
│  Output Tokens                                  598.4K │
│  Cache Read                                     131.2M │
│  Cache Write                                         0 │
│  Cost                                          $0.8828 │
│ opencode-go/glm-5.2                                    │
│  Messages                                          763 │
│  Input Tokens                                    14.6M │
│  Output Tokens                                  345.3K │
│  Cache Read                                      74.9M │
│  Cache Write                                         0 │
│  Cost                                         $41.4002 │
""";
        var parsed = OpenCodeStatsParser.ParseGoModels(output);

        Assert.Equal(2, parsed.Count);
        Assert.Equal(982, parsed["deepseek-v4-flash"].Messages);
        Assert.Equal(2_100_000, parsed["deepseek-v4-flash"].Input, 1.0);
        Assert.Equal(598_400, parsed["deepseek-v4-flash"].Output, 1.0);
        Assert.Equal(131_200_000, parsed["deepseek-v4-flash"].CacheRead, 1.0);
        Assert.Equal(763, parsed["glm-5.2"].Messages);
    }

    [Fact]
    public void ApproximatelyEqual_MatchesRoundedCliValues()
    {
        // 实测样本：本地 2145431 ↔ 官方 2.1M；本地 598353（含推理） ↔ 官方 598.4K
        Assert.True(OpenCodeStatsParser.ApproximatelyEqual(2_100_000, 2_145_431));
        Assert.True(OpenCodeStatsParser.ApproximatelyEqual(598_400, 598_353));
        Assert.True(OpenCodeStatsParser.ApproximatelyEqual(982, 982));
        Assert.False(OpenCodeStatsParser.ApproximatelyEqual(598_400, 285_527));
        Assert.False(OpenCodeStatsParser.ApproximatelyEqual(831_700, 14_600_000));
    }

    [Fact]
    public void UnavailableReport_IsDistinctFromZero()
    {
        var report = TokenUsageReport.Unavailable("未发现本机 OpenCode/Zcode 会话数据");
        Assert.False(report.Available);
        Assert.Empty(report.Clients);
        Assert.Contains("未发现", report.Detail);
    }

    [Fact]
    public void ClientUnavailable_IsDistinctFromZero_AndClientsStaySeparate()
    {
        // 某客户端不可用 / 无记录 ≠ 账号用量为 0
        var client = ClientTokenUsage.Unavailable("Zcode", "~/.zcode/cli/db/db.sqlite", "未发现会话库");
        Assert.False(client.Available);
        Assert.Empty(client.Records);
        Assert.Equal("Zcode", client.ClientName);
        Assert.Contains("未发现", client.Detail);

        // 报告按客户端分组：各客户端独立给出结果，没有跨客户端合并的总记录列表
        var other = ClientTokenUsage.Unavailable("OpenCode", "~/.local/share/opencode", "未发现会话数据目录");
        var report = new TokenUsageReport
        {
            Available = true,
            Clients = new[] { other, client },
        };
        Assert.Equal(2, report.Clients.Count);
        Assert.Equal("OpenCode", report.Clients[0].ClientName);
        Assert.Equal("Zcode", report.Clients[1].ClientName);
    }
}
