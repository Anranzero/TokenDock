using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace TokenDock;

/// <summary>GLM Coding Plan 提供方：国际版 Z.ai / 国内版 BigModel（Base URL 与账号完全独立，不混用）。</summary>
public enum GlmProvider
{
    Zai,
    BigModel
}

/// <summary>GLM Coding Plan 远端接口地址。BigModel 与 Z.ai 是两套接口族（均已实测）。</summary>
public static class GlmEndpoints
{
    /// <summary>额度查询：data.limits[]（type/number/usage/remaining/percentage/nextResetTime）。两家相同。</summary>
    public const string QuotaPath = "/api/monitor/usage/quota/limit";

    /// <summary>BigModel 模型用量：data.modelDataList[]（modelName/totalTokens）+ totalUsage 汇总。</summary>
    public const string BigModelModelUsagePath = "/api/monitor/usage/model-usage";

    /// <summary>BigModel 工具用量：data.toolDataList[]（toolName/totalUsageCount）。</summary>
    public const string BigModelToolUsagePath = "/api/monitor/usage/tool-usage";

    /// <summary>Z.ai MCP 额度：data.total_usage{used,limit,remaining} + level/server_time/next_refresh_at。</summary>
    public const string ZaiMcpUsagePath = "/api/v1/mcp/usage";

    /// <summary>Z.ai 按用量明细（usageType=MODEL|MCP）：data.modelUsage.modelDataList[] / data.mcpUsage.mcpDataList[]。</summary>
    public const string ZaiCreditUsageDetailPath = "/api/monitor/usage/credit-usage/usage-detail";

    /// <summary>Z.ai 每日用量汇总：data.summary{totalTokens,...} + data.series[]。</summary>
    public const string ZaiCreditUsageActivityPath = "/api/monitor/usage/credit-usage/activity";

    public static string BaseUrl(GlmProvider provider) => provider switch
    {
        GlmProvider.Zai => "https://api.z.ai",
        _ => "https://open.bigmodel.cn",
    };

    public static string DisplayName(GlmProvider provider) => provider switch
    {
        GlmProvider.Zai => "Z.ai（国际版）",
        _ => "BigModel（国内版）",
    };

    public static string ApiKeyHelpUrl(GlmProvider provider) => provider switch
    {
        GlmProvider.Zai => "https://z.ai/manage-apikey/apikey-list",
        _ => "https://open.bigmodel.cn/usercenter/apikeys",
    };
}

/// <summary>一条额度限制（接口原样字段；缺什么显示什么，不推算）。</summary>
public sealed class GlmQuotaLimit
{
    public string Type { get; init; } = "";
    public double? Unit { get; init; }
    public double? Number { get; init; }
    public double? Usage { get; init; }
    public double? CurrentValue { get; init; }
    public double? Remaining { get; init; }

    /// <summary>接口返回的 percentage 原值（口径以 --glmcheck 实测为准，不做二次换算）。</summary>
    public double? Percentage { get; init; }

    public DateTimeOffset? NextResetTime { get; init; }
}

/// <summary>MCP 额度（接口返回 used/limit/remaining，不推算）。</summary>
public sealed class GlmMcpQuota
{
    public double? Used { get; init; }
    public double? Limit { get; init; }
    public double? Remaining { get; init; }
    public string? Level { get; init; }
    public DateTimeOffset? NextRefreshAt { get; init; }
}

/// <summary>单个模型的 Token 用量（接口提供什么展示什么：分别展示输入/输出/缓存，缺则留空）。</summary>
public sealed class GlmModelUsage
{
    public string ModelName { get; init; } = "";
    public double? TotalTokens { get; init; }
    public double? CachedInputTokens { get; init; }
    public double? UncachedInputTokens { get; init; }
    public double? OutputTokens { get; init; }
    public double? TotalCredits { get; init; }
}

/// <summary>MCP 工具调用（mcpDataList：调用次数与额度消耗）。</summary>
public sealed class GlmMcpToolUsage
{
    public string Name { get; init; } = "";
    public double? CallCount { get; init; }
    public double? TotalCredits { get; init; }
}

/// <summary>一次 GLM Coding Plan 采集的账号级数据（全部来自远端接口）。</summary>
public sealed record GlmUsageData
{
    public GlmProvider Provider { get; init; }
    public string? Level { get; init; }
    public IReadOnlyList<GlmQuotaLimit> Limits { get; init; } = Array.Empty<GlmQuotaLimit>();
    public GlmMcpQuota? McpQuota { get; init; }
    public IReadOnlyList<GlmModelUsage> Models { get; init; } = Array.Empty<GlmModelUsage>();
    public IReadOnlyList<GlmMcpToolUsage> McpTools { get; init; } = Array.Empty<GlmMcpToolUsage>();

    /// <summary>区间总 Token（接口 modelUsage 汇总，缺失为 null 而不是 0）。</summary>
    public double? TotalTokens { get; init; }

    /// <summary>区间总模型调用次数（接口返回时展示，BigModel = totalUsage.totalModelCallCount）。</summary>
    public double? TotalModelCallCount { get; init; }

    /// <summary>区间总 MCP 调用次数（接口返回时展示）。</summary>
    public double? TotalMcpCallCount { get; init; }

    /// <summary>日汇总（activity.series）起止，用于在 UI 标注统计范围。</summary>
    public DateTimeOffset? RangeStart { get; init; }
    public DateTimeOffset? RangeEnd { get; init; }

    public DateTimeOffset FetchedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>本次采集里未取到的可选接口（用于在 UI 注明"未提供/未获取"）。</summary>
    public string? PartialNote { get; init; }
}

/// <summary>GLM 采集结果（失败分类复用 FetchFailureKind）。</summary>
public sealed class GlmFetchResult
{
    public bool Success { get; private init; }
    public GlmUsageData? Data { get; private init; }
    public FetchFailureKind FailureKind { get; private init; }
    public string? ErrorMessage { get; private init; }
    public string? ServerDetail { get; private init; }

    public static GlmFetchResult Ok(GlmUsageData data) => new() { Success = true, Data = data };

    public static GlmFetchResult Fail(FetchFailureKind kind, string message, string? detail = null)
        => new() { Success = false, FailureKind = kind, ErrorMessage = message, ServerDetail = detail };
}

/// <summary>
/// GLM Coding Plan 远端客户端：只用账号级远端接口，绝不读取本机会话数据库。
/// 鉴权：Authorization: Bearer &lt;key&gt;（并同时带 X-Bigmodel-Authorization，覆盖 ZCode 使用的两套方案），
/// 具体以 --glmcheck 实测为准。
/// </summary>
public sealed class GlmUsageClient : IDisposable
{
    private readonly HttpClient _http;

    public GlmUsageClient() : this(CreateHttpClient())
    {
    }

    public GlmUsageClient(HttpClient http) => _http = http;

    public void Dispose() => _http.Dispose();

    private static HttpClient CreateHttpClient()
    {
        var http = new HttpClient(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            ConnectTimeout = TimeSpan.FromSeconds(10),
        })
        {
            Timeout = TimeSpan.FromSeconds(20),
        };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("TokenDock/1.0");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        return http;
    }

    /// <summary>采集账号级数据：额度（必需）+ MCP 额度 + 模型/工具明细（可选，失败不影响主数据）。</summary>
    public async Task<GlmFetchResult> FetchAsync(GlmProvider provider, string? apiKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return GlmFetchResult.Fail(FetchFailureKind.NotConfigured,
                $"尚未设置 {GlmEndpoints.DisplayName(provider)} 密钥。");

        var (failure, quotaBody) = await GetRawAsync(provider, apiKey, GlmEndpoints.QuotaPath, query: null, cancellationToken);
        if (failure is not null)
            return failure;

        var missing = new List<string>();
        var now = DateTimeOffset.UtcNow;
        var rangeStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var monthQuery = "type=1&startTime=" + Uri.EscapeDataString(rangeStart.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))
            + "&endTime=" + Uri.EscapeDataString(now.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));

        string? mcpBody = null, modelBody = null, toolBody = null, activityBody = null;
        if (provider == GlmProvider.BigModel)
        {
            // BigModel 实测接口族（2026-09-17 用真实密钥逐条验证）
            modelBody = await GetOptionalAsync(provider, apiKey, GlmEndpoints.BigModelModelUsagePath, monthQuery, missing, "模型用量", cancellationToken);
            toolBody = await GetOptionalAsync(provider, apiKey, GlmEndpoints.BigModelToolUsagePath, monthQuery, missing, "工具用量", cancellationToken);
        }
        else
        {
            // Z.ai 接口族（ZCode 客户端代码确认；以上 credit-usage / mcp 路径在 BigModel 上均为 404）
            mcpBody = await GetOptionalAsync(provider, apiKey, GlmEndpoints.ZaiMcpUsagePath, query: null, missing, "MCP 额度", cancellationToken);
            modelBody = await GetOptionalAsync(provider, apiKey, GlmEndpoints.ZaiCreditUsageDetailPath,
                monthQuery + "&usageType=MODEL", missing, "模型用量明细", cancellationToken);
            toolBody = await GetOptionalAsync(provider, apiKey, GlmEndpoints.ZaiCreditUsageDetailPath,
                monthQuery + "&usageType=MCP", missing, "MCP 用量明细", cancellationToken);
            activityBody = await GetOptionalAsync(provider, apiKey, GlmEndpoints.ZaiCreditUsageActivityPath,
                monthQuery, missing, "用量汇总", cancellationToken);
        }

        var parsed = GlmUsageParser.Parse(provider, quotaBody!, mcpBody, modelBody, toolBody, activityBody);
        return GlmFetchResult.Ok(parsed with
        {
            PartialNote = missing.Count == 0 ? null : "未获取：" + string.Join("、", missing),
        });
    }

    /// <summary>发起请求并分类失败；成功时返回响应体（供解析）。</summary>
    private async Task<(GlmFetchResult? Failure, string? Body)> GetRawAsync(
        GlmProvider provider, string apiKey, string path, string? query, CancellationToken ct)
    {
        var url = GlmEndpoints.BaseUrl(provider) + path + (string.IsNullOrEmpty(query) ? "" : "?" + query);
        HttpResponseMessage response;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            ApplyAuth(request.Headers, apiKey);
            response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return (GlmFetchResult.Fail(FetchFailureKind.NetworkError, "连接超时（20 秒内未收到响应），请检查网络。"), null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            return (GlmFetchResult.Fail(FetchFailureKind.NetworkError, $"网络连接失败：{ex.Message}"), null);
        }
        catch (Exception ex)
        {
            return (GlmFetchResult.Fail(FetchFailureKind.NetworkError, $"请求出现异常：{ex.GetType().Name} {ex.Message}"), null);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            return (Classify(response.StatusCode, body), body);
        }
    }

    /// <summary>可选接口：失败只记录，不影响主数据（保留最后成功数据由上层状态负责）。</summary>
    private async Task<string?> GetOptionalAsync(
        GlmProvider provider, string apiKey, string path, string? query, List<string> missing, string label, CancellationToken ct)
    {
        try
        {
            var (failure, body) = await GetRawAsync(provider, apiKey, path, query, ct);
            if (failure is null) return body;
            missing.Add(label + "（" + failure.ErrorMessage + "）");
            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            missing.Add(label + "（" + ex.Message + "）");
            return null;
        }
    }

    /// <summary>鉴权头：Authorization + X-Bigmodel-Authorization（两套方案同时带，实测确认后收敛）。</summary>
    internal static void ApplyAuth(System.Net.Http.Headers.HttpRequestHeaders headers, string apiKey)
    {
        headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        headers.TryAddWithoutValidation("X-Bigmodel-Authorization", "Bearer " + apiKey);
    }

    /// <summary>诊断用：原样取回某个接口的响应体（--glmcheck 打印字段用，不做解析）。</summary>
    public async Task<(int StatusCode, string? Body, string? Error)> FetchRawAsync(
        GlmProvider provider, string apiKey, string path, string? query, CancellationToken ct = default)
    {
        try
        {
            var url = GlmEndpoints.BaseUrl(provider) + path + (string.IsNullOrEmpty(query) ? "" : "?" + query);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            ApplyAuth(request.Headers, apiKey);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            return ((int)response.StatusCode, await response.Content.ReadAsStringAsync(ct), null);
        }
        catch (Exception ex)
        {
            return (0, null, ex.Message);
        }
    }

    /// <summary>HTTP 状态与业务错误分类（公开静态以便测试）。</summary>
    public static GlmFetchResult? Classify(HttpStatusCode statusCode, string body)
    {
        var detail = GlmUsageParser.ExtractServerMessage(body);
        switch (statusCode)
        {
            case HttpStatusCode.OK:
                return null;
            case HttpStatusCode.Unauthorized:
                return GlmFetchResult.Fail(FetchFailureKind.InvalidKey,
                    "GLM 密钥无效或已失效（HTTP 401），请重新设置。", detail);
            case HttpStatusCode.Forbidden:
                return GlmFetchResult.Fail(FetchFailureKind.NoSubscription,
                    "无权限访问（HTTP 403）：可能未订阅 Coding Plan 或密钥类型不符。", detail);
            case HttpStatusCode.PaymentRequired:
                return GlmFetchResult.Fail(FetchFailureKind.NoSubscription,
                    "订阅不可用（HTTP 402），请确认 Coding Plan 订阅状态。", detail);
            case HttpStatusCode.TooManyRequests:
                return GlmFetchResult.Fail(FetchFailureKind.RateLimited,
                    "请求过于频繁（HTTP 429），请稍后重试。", detail);
        }

        if ((int)statusCode >= 500)
            return GlmFetchResult.Fail(FetchFailureKind.ServerError, $"服务端异常（HTTP {(int)statusCode}），请稍后重试。", detail);

        // 业务错误也可能走 HTTP 200：code != 0 时按消息识别“无套餐/无资格”
        if (statusCode == HttpStatusCode.OK && detail is not null && GlmUsageParser.LooksLikeNoPlan(detail))
            return GlmFetchResult.Fail(FetchFailureKind.NoSubscription, "该账号未订阅 Coding Plan（或套餐已过期）。", detail);

        return GlmFetchResult.Fail(FetchFailureKind.ServerError, $"接口返回异常状态码（HTTP {(int)statusCode}）。", detail);
    }
}

/// <summary>GLM 响应解析（字段名全部来自 ZCode 客户端代码，未提供的字段一律留空，不推算）。</summary>
public static class GlmUsageParser
{
    /// <summary>业务“无套餐/无资格”消息（ZCode 同款判定）。</summary>
    public static bool LooksLikeNoPlan(string message)
        => message.Contains("不存在coding plan", StringComparison.OrdinalIgnoreCase)
           || message.Contains("没有资格", StringComparison.Ordinal)
           || message.Contains("no coding plan", StringComparison.OrdinalIgnoreCase);

    /// <summary>从错误体提取 error.message / msg。</summary>
    public static string? ExtractServerMessage(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (root.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.Object
                && err.TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.String)
                return msg.GetString();
            if (root.TryGetProperty("msg", out var m) && m.ValueKind == JsonValueKind.String)
                return m.GetString();
            if (root.TryGetProperty("message", out var m2) && m2.ValueKind == JsonValueKind.String)
                return m2.GetString();
        }
        catch (JsonException)
        {
            // 非 JSON，忽略
        }

        return null;
    }

    /// <summary>解析各接口响应并组装账号级数据（缺接口时对应部分留空；兼容 BigModel / Z.ai 两套形态）。</summary>
    public static GlmUsageData Parse(
        GlmProvider provider, string quotaBody,
        string? mcpBody, string? modelBody, string? toolBody, string? activityBody)
    {
        var quotaData = DataOf(quotaBody);
        var limits = ReadLimits(quotaData);
        var level = quotaData is { } qd && qd.TryGetProperty("level", out var lv) && lv.ValueKind == JsonValueKind.String
            ? lv.GetString()
            : null;

        // Z.ai MCP 额度（/api/v1/mcp/usage；BigModel 无此接口）
        GlmMcpQuota? mcpQuota = null;
        if (DataOf(mcpBody) is { } mcpData && mcpData.TryGetProperty("total_usage", out var totalUsage)
            && totalUsage.ValueKind == JsonValueKind.Object)
        {
            mcpQuota = new GlmMcpQuota
            {
                Used = Number(totalUsage, "used"),
                Limit = Number(totalUsage, "limit"),
                Remaining = Number(totalUsage, "remaining"),
                Level = Str(mcpData, "level"),
                NextRefreshAt = UnixMs(mcpData, "next_refresh_at"),
            };
        }

        // 模型明细：BigModel = data.modelDataList[]（仅 totalTokens）；Z.ai = data.modelUsage.modelDataList[]
        var models = new List<GlmModelUsage>();
        double? totalTokens = null;
        double? totalModelCalls = null;
        DateTimeOffset? rangeStart = null;
        DateTimeOffset? rangeEnd = null;
        if (DataOf(modelBody) is { } md)
        {
            var list = modelBody is null
                ? default
                : TryGetArray(md, "modelUsage", "modelDataList") ?? TryGetArray(md, "modelDataList");
            if (list is { } array)
            {
                foreach (var item in array.EnumerateArray())
                {
                    var name = Str(item, "modelName") ?? Str(item, "modelCode");
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    models.Add(new GlmModelUsage
                    {
                        ModelName = name,
                        // 标量优先：BigModel 的 tokensUsage 是按天数组、totalTokens 才是区间合计；
                        // Z.ai 的 totalTokensUsage/tokensUsage 可能是标量或数组——只有全无标量时才退化为求和
                        TotalTokens = NumberPreferScalar(item, "totalTokens", "totalTokensUsage", "tokensUsage"),
                        CachedInputTokens = NumberPreferScalar(item, "cachedInputTokensUsage"),
                        UncachedInputTokens = NumberPreferScalar(item, "uncachedInputTokensUsage"),
                        OutputTokens = NumberPreferScalar(item, "outputTokensUsage"),
                        TotalCredits = NumberPreferScalar(item, "totalCreditsUsage"),
                    });
                }
            }

            if (md.TryGetProperty("totalUsage", out var tu) && tu.ValueKind == JsonValueKind.Object)
            {
                totalTokens = Number(tu, "totalTokensUsage");
                totalModelCalls = Number(tu, "totalModelCallCount");
            }

            totalTokens ??= Sum(models.Select(m => m.TotalTokens));
            (rangeStart, rangeEnd) = ReadXTimeRange(md);
        }

        // 工具/MCP 明细：BigModel = data.toolDataList[]；Z.ai = data.mcpUsage.mcpDataList[]
        var tools = new List<GlmMcpToolUsage>();
        double? totalMcpCalls = null;
        if (DataOf(toolBody) is { } td)
        {
            var list = TryGetArray(td, "mcpUsage", "mcpDataList") ?? TryGetArray(td, "toolDataList");
            if (list is { } array)
            {
                foreach (var item in array.EnumerateArray())
                {
                    var name = Str(item, "toolName") ?? Str(item, "mcpName") ?? Str(item, "toolCode") ?? Str(item, "mcpCode");
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    tools.Add(new GlmMcpToolUsage
                    {
                        Name = name,
                        CallCount = Number(item, "totalUsageCount") ?? Number(item, "mcpCallCount") ?? Number(item, "usageCount"),
                        TotalCredits = Number(item, "totalCredits"),
                    });
                }
            }

            if (td.TryGetProperty("totalUsage", out var toolTotal) && toolTotal.ValueKind == JsonValueKind.Object)
                totalMcpCalls = Number(toolTotal, "totalSearchMcpCount");
            totalMcpCalls ??= Sum(tools.Select(t => t.CallCount));
            (rangeStart, rangeEnd) = rangeStart is null ? ReadXTimeRange(td) : (rangeStart, rangeEnd);
        }

        // Z.ai 每日汇总（series 的日期范围；BigModel 的范围来自 x_time）
        if (DataOf(activityBody) is { } act && act.TryGetProperty("series", out var series) && series.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in series.EnumerateArray())
            {
                var date = Str(item, "date");
                if (date is null || !DateTimeOffset.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d))
                    continue;
                if (rangeStart is null || d < rangeStart) rangeStart = d;
                if (rangeEnd is null || d > rangeEnd) rangeEnd = d;
            }
        }

        return new GlmUsageData
        {
            Provider = provider,
            Level = level,
            Limits = limits,
            McpQuota = mcpQuota,
            Models = models,
            McpTools = tools,
            TotalTokens = totalTokens,
            TotalModelCallCount = totalModelCalls,
            TotalMcpCallCount = totalMcpCalls,
            RangeStart = rangeStart,
            RangeEnd = rangeEnd,
        };
    }

    /// <summary>取嵌套数组（如 data.modelUsage.modelDataList）；不存在时返回 null。</summary>
    private static JsonElement? TryGetArray(JsonElement root, string objectName, string arrayName)
    {
        if (!root.TryGetProperty(objectName, out var obj) || obj.ValueKind != JsonValueKind.Object)
            return null;
        return obj.TryGetProperty(arrayName, out var array) && array.ValueKind == JsonValueKind.Array ? array : null;
    }

    /// <summary>取顶层数组（如 data.modelDataList）；不存在时返回 null。</summary>
    private static JsonElement? TryGetArray(JsonElement root, string arrayName)
        => root.TryGetProperty(arrayName, out var array) && array.ValueKind == JsonValueKind.Array ? array : null;

    /// <summary>BigModel 的 x_time 日期数组 → 统计范围。</summary>
    private static (DateTimeOffset? Start, DateTimeOffset? End) ReadXTimeRange(JsonElement data)
    {
        if (!data.TryGetProperty("x_time", out var xTime) || xTime.ValueKind != JsonValueKind.Array)
            return (null, null);
        DateTimeOffset? start = null;
        DateTimeOffset? end = null;
        foreach (var item in xTime.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String) continue;
            if (!DateTimeOffset.TryParse(item.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d))
                continue;
            if (start is null || d < start) start = d;
            if (end is null || d > end) end = d;
        }

        return (start, end);
    }

    private static IReadOnlyList<GlmQuotaLimit> ReadLimits(JsonElement? data)
    {
        var result = new List<GlmQuotaLimit>();
        if (data is not { } d || !d.TryGetProperty("limits", out var limits) || limits.ValueKind != JsonValueKind.Array)
            return result;
        foreach (var item in limits.EnumerateArray())
        {
            var type = Str(item, "type");
            if (string.IsNullOrWhiteSpace(type)) continue;
            result.Add(new GlmQuotaLimit
            {
                Type = type,
                Unit = Number(item, "unit"),
                Number = Number(item, "number"),
                Usage = Number(item, "usage"),
                CurrentValue = Number(item, "currentValue"),
                Remaining = Number(item, "remaining"),
                Percentage = Number(item, "percentage"),
                NextResetTime = UnixMs(item, "nextResetTime"),
            });
        }

        return result;
    }

    /// <summary>
    /// 取响应 data 节点。成功码两家不同：Z.ai 用 code=0，BigModel 用 code=200（均含 success:true）；
    /// 其他 code 或缺少 data 视为业务失败。
    /// </summary>
    private static JsonElement? DataOf(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (root.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.Number)
            {
                var value = code.GetInt32();
                if (value != 0 && value != 200) return null;
            }

            return root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object ? data.Clone() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// 按名称顺序读数值并**优先取标量**：先逐个尝试标量（number/数字字符串），
    /// 全都不是标量时才把数组当作时间序列求和（接口对同一字段存在两种形态）。
    /// </summary>
    private static double? NumberPreferScalar(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out var value)) continue;
            if (value.ValueKind is JsonValueKind.Number or JsonValueKind.String)
            {
                var scalar = Scalar(value);
                if (scalar is not null) return scalar;
            }
        }

        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array)
            {
                var sum = Scalar(value);
                if (sum is not null) return sum;
            }
        }

        return null;
    }

    private static string? Str(JsonElement element, string name)
        => element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>数值读取：兼容 number 与数字字符串；数组时求和（接口对累计值有两种形态）。</summary>
    private static double? Number(JsonElement element, params string[] path)
    {
        var current = element;
        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out var next))
                return null;
            current = next;
        }

        return Scalar(current);
    }

    private static double? Scalar(JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Number when value.TryGetDouble(out var d):
                return d;
            case JsonValueKind.String when double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var s):
                return s;
            case JsonValueKind.Array:
            {
                double sum = 0;
                var any = false;
                foreach (var item in value.EnumerateArray())
                {
                    var v = Scalar(item);
                    if (v is null) continue;
                    sum += v.Value;
                    any = true;
                }

                return any ? sum : null;
            }
            default:
                return null;
        }
    }

    private static DateTimeOffset? UnixMs(JsonElement element, string name)
    {
        var value = Number(element, name);
        if (value is null) return null;
        var ms = value.Value;
        if (ms <= 0) return null;
        // 秒 / 毫秒自动识别（ZCode 侧按毫秒使用 *1000 转换，这里做保守判断）
        return ms > 1_000_000_000_000 ? DateTimeOffset.FromUnixTimeMilliseconds((long)ms) : DateTimeOffset.FromUnixTimeSeconds((long)ms);
    }

    private static double? Sum(IEnumerable<double?> values)
    {
        double sum = 0;
        var any = false;
        foreach (var v in values)
        {
            if (v is null) continue;
            sum += v.Value;
            any = true;
        }

        return any ? sum : null;
    }
}

/// <summary>GLM 页状态：保存最近一次成功数据，失败保留旧数据并标记过期（与 OpenCode 页一致）。</summary>
public sealed class GlmState
{
    public GlmUsageData? LastGood { get; private set; }
    public bool IsStale { get; private set; }
    public string StatusText { get; private set; } = "尚未获取数据";
    public FetchFailureKind LastFailureKind { get; private set; } = FetchFailureKind.None;

    public void Apply(GlmFetchResult result)
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
            StatusText = result.FailureKind == FetchFailureKind.NotConfigured
                ? "尚未配置：" + result.ErrorMessage
                : $"✖ 获取失败：{result.ErrorMessage}";
        }
        else
        {
            IsStale = true;
            StatusText = $"⚠ 数据已过期 · {result.ErrorMessage}"
                + $" · 最后成功：{LastGood.FetchedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}";
        }
    }
}
