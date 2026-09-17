using System.Net;
using Xunit;

namespace TokenDock.Tests;

/// <summary>
/// GLM Coding Plan 解析测试：字段名与结构全部来自 ZCode 客户端实测（limits / total_usage /
/// modelDataList / mcpDataList），缺失字段必须留空而不是推算。
/// </summary>
public class GlmTests
{
    private const string QuotaJson = """
    {"code":0,"msg":"ok","data":{"level":"individual-coding-plan","limits":[
      {"type":"TOKENS_LIMIT","unit":3,"number":5,"usage":42,"remaining":58,"percentage":42,"nextResetTime":1789000000000},
      {"type":"TOKENS_LIMIT","unit":6,"usage":21,"remaining":79,"percentage":21,"nextResetTime":1789500000000},
      {"type":"TIME_LIMIT","unit":5,"number":1,"usage":136,"remaining":864,"percentage":13.6}
    ]}}
    """;

    private const string McpJson = """
    {"code":0,"msg":"ok","data":{"server_time":1788800000,"next_refresh_at":1789800000000,"level":"basic",
      "total_usage":{"used":136,"limit":1000,"remaining":864}}}
    """;

    private const string ModelDetailJson = """
    {"code":0,"data":{"modelUsage":{"modelDataList":[
      {"modelName":"GLM-5.3","cachedInputTokensUsage":12480000,"uncachedInputTokensUsage":1240000,"outputTokensUsage":486000,"totalTokensUsage":14206000,"totalCreditsUsage":42.6},
      {"modelCode":"GLM-5.3-Flash","cachedInputTokensUsage":[8000000,920000],"uncachedInputTokensUsage":640000,"outputTokensUsage":291000,"totalTokensUsage":9851000,"totalCreditsUsage":18.2},
      {"modelName":"  ","totalTokensUsage":999}
    ]}}}
    """;

    private const string McpDetailJson = """
    {"code":0,"data":{"mcpUsage":{"mcpDataList":[
      {"mcpName":"web-search","mcpCallCount":96,"totalCredits":9.6},
      {"mcpCode":"web-reader","totalUsageCount":40,"totalCredits":4.0},
      {"toolName":"","mcpCallCount":5}
    ]}}}
    """;

    private const string ActivityJson = """
    {"code":0,"data":{"summary":{"totalTokens":27787000},"series":[
      {"date":"2026-09-01","totalTokens":1200000,"modelCallCount":18,"mcpCalls":3},
      {"date":"2026-09-17","totalTokens":980000,"modelCallCount":12,"mcpCalls":1}
    ]}}
    """;

    private static GlmUsageData ParseAll()
        => GlmUsageParser.Parse(GlmProvider.Zai, QuotaJson, McpJson, ModelDetailJson, McpDetailJson, ActivityJson);

    [Fact]
    public void Parse_ReadsLimitsAndLevel()
    {
        var data = ParseAll();
        Assert.Equal("individual-coding-plan", data.Level);
        Assert.Equal(3, data.Limits.Count);
        Assert.Equal("TOKENS_LIMIT", data.Limits[0].Type);
        Assert.Equal(42, data.Limits[0].Percentage);
        Assert.Equal(58, data.Limits[0].Remaining);
        Assert.NotNull(data.Limits[0].NextResetTime);
    }

    [Fact]
    public void Parse_ReadsMcpQuota()
    {
        var data = ParseAll();
        Assert.NotNull(data.McpQuota);
        Assert.Equal(136, data.McpQuota!.Used);
        Assert.Equal(1000, data.McpQuota.Limit);
        Assert.Equal(864, data.McpQuota.Remaining);
        Assert.Equal("basic", data.McpQuota.Level);
        Assert.NotNull(data.McpQuota.NextRefreshAt);
    }

    [Fact]
    public void Parse_ReadsModels_DynamicNames_AndSumsArrays()
    {
        var data = ParseAll();
        // 空名字被过滤，动态模型名保留
        Assert.Equal(2, data.Models.Count);
        Assert.Equal("GLM-5.3", data.Models[0].ModelName);
        Assert.Equal("GLM-5.3-Flash", data.Models[1].ModelName);
        // 数组形态的序列值按求和解释（接口两种形态都兼容）
        Assert.Equal(8_920_000, data.Models[1].CachedInputTokens);
        Assert.Equal(14_206_000 + 9_851_000, data.TotalTokens);
    }

    [Fact]
    public void Parse_ReadsMcpTools_AndCallCounts()
    {
        var data = ParseAll();
        Assert.Equal(2, data.McpTools.Count);
        Assert.Equal("web-search", data.McpTools[0].Name);
        Assert.Equal(96, data.McpTools[0].CallCount);
        Assert.Equal(40, data.McpTools[1].CallCount);
        Assert.Equal(136, data.TotalMcpCallCount);
    }

    [Fact]
    public void Parse_ReadsActivityRange()
    {
        var data = ParseAll();
        Assert.Equal(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero), data.RangeStart);
        Assert.Equal(new DateTimeOffset(2026, 9, 17, 0, 0, 0, TimeSpan.Zero), data.RangeEnd);
    }

    [Fact]
    public void Parse_MissingOptionalSections_LeaveNullsNotZero()
    {
        var data = GlmUsageParser.Parse(GlmProvider.BigModel, QuotaJson, null, null, null, null);
        Assert.Null(data.McpQuota);
        Assert.Empty(data.Models);
        Assert.Null(data.TotalTokens);
        Assert.Null(data.TotalMcpCallCount);
        Assert.Null(data.RangeStart);
    }

    [Fact]
    public void Parse_BusinessErrorCode_IgnoresData()
    {
        var data = GlmUsageParser.Parse(GlmProvider.Zai, "{\"code\":1001,\"msg\":\"no coding plan\"}", null, null, null, null);
        Assert.Empty(data.Limits);
        Assert.Null(data.Level);
    }

    [Theory]
    [InlineData("TOKENS_LIMIT", 3d, 5d, "5 小时剩余")]
    [InlineData("TOKENS_LIMIT", 6d, null, "每周剩余")]
    [InlineData("CREDIT_LIMIT", 3d, 5d, "5 小时剩余")]   // BigModel 实测类型：与 TOKENS_LIMIT 等价
    [InlineData("CREDIT_LIMIT", 6d, 1d, "每周剩余")]
    [InlineData("TIME_LIMIT", 5d, 1d, "工具调用（MCP）")]
    [InlineData("CREDIT_LIMIT", 9d, null, "Token 额度（9）")] // 等价组内未命中三元组
    [InlineData("WEIRD_LIMIT", 9d, null, "WEIRD_LIMIT")]      // 完全未知类型 → 原样显示，不臆测
    public void LimitLabel_MapsConfirmedTriplesOnly(string type, double? unit, double? number, string expected)
    {
        var limit = new GlmQuotaLimit { Type = type, Unit = unit, Number = number };
        Assert.Equal(expected, GlmQuotaCard.GlmLimitLabel(limit));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "{}", FetchFailureKind.InvalidKey)]
    [InlineData(HttpStatusCode.Forbidden, "{}", FetchFailureKind.NoSubscription)]
    [InlineData(HttpStatusCode.PaymentRequired, "{}", FetchFailureKind.NoSubscription)]
    [InlineData(HttpStatusCode.TooManyRequests, "{}", FetchFailureKind.RateLimited)]
    [InlineData(HttpStatusCode.InternalServerError, "{}", FetchFailureKind.ServerError)]
    public void Classify_MapsStatusCodes(HttpStatusCode status, string body, FetchFailureKind expected)
    {
        var result = GlmUsageClient.Classify(status, body);
        Assert.NotNull(result);
        Assert.Equal(expected, result!.FailureKind);
    }

    [Fact]
    public void Classify_Ok_ReturnsNull()
        => Assert.Null(GlmUsageClient.Classify(HttpStatusCode.OK, "{\"code\":0,\"data\":{}}"));

    [Fact]
    public void NoPlanMessages_AreDetected()
    {
        Assert.True(GlmUsageParser.LooksLikeNoPlan("该账号不存在coding plan"));
        Assert.True(GlmUsageParser.LooksLikeNoPlan("没有资格使用"));
        Assert.True(GlmUsageParser.LooksLikeNoPlan("No coding plan found"));
        Assert.False(GlmUsageParser.LooksLikeNoPlan("rate limit exceeded"));
    }

    [Fact]
    public void ExtractServerMessage_ReadsMsgAndError()
    {
        Assert.Equal("额度不足", GlmUsageParser.ExtractServerMessage("{\"code\":1,\"msg\":\"额度不足\"}"));
        Assert.Equal("Unauthorized", GlmUsageParser.ExtractServerMessage("{\"error\":{\"message\":\"Unauthorized\"}}"));
    }

    [Fact]
    public void GlmState_KeepsLastGoodOnFailure_AndMarksStale()
    {
        var state = new GlmState();
        state.Apply(GlmFetchResult.Ok(ParseAll()));
        Assert.False(state.IsStale);

        state.Apply(GlmFetchResult.Fail(FetchFailureKind.NetworkError, "网络连接失败"));
        Assert.True(state.IsStale);
        Assert.NotNull(state.LastGood);
        Assert.Contains("已过期", state.StatusText);
    }

    [Fact]
    public void GlmState_NotConfiguredWithoutData_ShowsHint()
    {
        var state = new GlmState();
        state.Apply(GlmFetchResult.Fail(FetchFailureKind.NotConfigured, "尚未设置密钥"));
        Assert.False(state.IsStale);
        Assert.Null(state.LastGood);
        Assert.Equal(FetchFailureKind.NotConfigured, state.LastFailureKind);
    }

    [Theory]
    [InlineData(GlmProvider.Zai, "https://api.z.ai")]
    [InlineData(GlmProvider.BigModel, "https://open.bigmodel.cn")]
    public void Endpoints_BaseUrlsAreProviderSpecific(GlmProvider provider, string expected)
        => Assert.Equal(expected, GlmEndpoints.BaseUrl(provider));
}

/// <summary>
/// BigModel 实测回归（2026-09-17 用真实密钥抓取的响应原样入夹具）：
/// 成功码为 code=200、额度类型为 CREDIT_LIMIT、模型/工具明细走 model-usage / tool-usage。
/// </summary>
public class GlmBigModelTests
{
    private const string QuotaJson = """
    {"code":200,"msg":"操作成功","data":{"limits":[
      {"type":"CREDIT_LIMIT","unit":3,"number":5,"usage":2000,"currentValue":814,"remaining":1185,"percentage":40,"nextResetTime":1789658904374},
      {"type":"CREDIT_LIMIT","unit":6,"number":1,"usage":10000,"currentValue":4824,"remaining":5175,"percentage":48,"nextResetTime":1790183449975}
    ],"level":"lite"},"success":true}
    """;

    private const string ModelUsageJson = """
    {"code":200,"msg":"操作成功","data":{
      "x_time":["2026-09-01","2026-09-16","2026-09-17"],
      "modelCallCount":[243,168,377],
      "tokensUsage":[29391678,17112834,138287692],
      "totalUsage":{"totalModelCallCount":4469,"totalTokensUsage":742540978},
      "modelDataList":[
        {"modelName":"GLM-5.3","sortOrder":1,"tokensUsage":[0,6474355,0],"totalTokens":37514731},
        {"modelName":"GLM-5.3-Flash","sortOrder":2,"tokensUsage":[29391678,10638479,138287692],"totalTokens":705026247}
      ],
      "granularity":"daily"},"success":true}
    """;

    private const string ToolUsageJson = """
    {"code":200,"msg":"操作成功","data":{
      "x_time":["2026-09-01","2026-09-17"],
      "totalUsage":{"totalNetworkSearchCount":10,"totalWebReadMcpCount":2,"totalSearchMcpCount":12},
      "toolDataList":[
        {"toolCode":"search-prime","toolName":"联网搜索 MCP","totalUsageCount":10,"sortOrder":1},
        {"toolCode":"web-reader","toolName":"网页读取 MCP","totalUsageCount":2,"sortOrder":2}
      ],"granularity":"daily"},"success":true}
    """;

    private static GlmUsageData Parse() =>
        GlmUsageParser.Parse(GlmProvider.BigModel, QuotaJson, mcpBody: null, ModelUsageJson, ToolUsageJson, activityBody: null);

    [Fact]
    public void SuccessCode200_IsAccepted()
    {
        var data = Parse();
        Assert.Equal(2, data.Limits.Count);
        Assert.Equal("lite", data.Level);
    }

    [Fact]
    public void CreditLimit_IsMappedLikeTokenLimit()
    {
        var data = Parse();
        Assert.Equal("5 小时剩余", GlmQuotaCard.GlmLimitLabel(data.Limits[0]));
        Assert.Equal("每周剩余", GlmQuotaCard.GlmLimitLabel(data.Limits[1]));
        Assert.Equal(40, data.Limits[0].Percentage);
        Assert.Equal(1185, data.Limits[0].Remaining);
        Assert.NotNull(data.Limits[0].NextResetTime);
    }

    [Fact]
    public void ModelUsage_GivesPerModelTotalsOnly()
    {
        var data = Parse();
        Assert.Equal(2, data.Models.Count);
        Assert.Equal("GLM-5.3", data.Models[0].ModelName);
        Assert.Equal(37_514_731, data.Models[0].TotalTokens);
        // BigModel 未提供输入/输出/缓存拆分 → 全部留空，不推算
        Assert.Null(data.Models[0].CachedInputTokens);
        Assert.Null(data.Models[0].UncachedInputTokens);
        Assert.Null(data.Models[0].OutputTokens);
        Assert.Equal(742_540_978, data.TotalTokens);
        Assert.Equal(4469, data.TotalModelCallCount);
    }

    [Fact]
    public void ToolUsage_GivesMcpCallCounts_AndRange()
    {
        var data = Parse();
        Assert.Equal(2, data.McpTools.Count);
        Assert.Equal("联网搜索 MCP", data.McpTools[0].Name);
        Assert.Equal(10, data.McpTools[0].CallCount);
        Assert.Equal(12, data.TotalMcpCallCount);
        Assert.Equal(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero), data.RangeStart);
        Assert.Equal(new DateTimeOffset(2026, 9, 17, 0, 0, 0, TimeSpan.Zero), data.RangeEnd);
        // BigModel 无 /api/v1/mcp/usage → MCP 额度卡不显示（null 而不是 0）
        Assert.Null(data.McpQuota);
    }

    [Fact]
    public void OldZeroCode_StillAccepted()
    {
        var data = GlmUsageParser.Parse(GlmProvider.Zai, "{\"code\":0,\"data\":{\"limits\":[{\"type\":\"TOKENS_LIMIT\",\"unit\":6,\"remaining\":1}]}}", null, null, null, null);
        Assert.Single(data.Limits);
    }
}
