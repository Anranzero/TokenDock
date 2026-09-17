using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace TokenDock;

/// <summary>OpenCode Go 用量接口客户端。接口：GET https://opencode.ai/zen/go/v1/usage（Bearer 鉴权）。</summary>
public sealed class UsageApiClient : IDisposable
{
    public const string DefaultEndpoint = "https://opencode.ai/zen/go/v1/usage";

    private readonly HttpClient _http;

    public UsageApiClient() : this(CreateHttpClient())
    {
    }

    public UsageApiClient(HttpClient http) => _http = http;

    public void Dispose() => _http.Dispose();

    private static HttpClient CreateHttpClient()
    {
        var http = new HttpClient(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            ConnectTimeout = TimeSpan.FromSeconds(10),
        })
        {
            Timeout = TimeSpan.FromSeconds(15),
        };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("TokenDock/1.0");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        return http;
    }

    /// <summary>使用 Bearer 密钥拉取用量。任何网络异常都会转为失败结果，不会抛出（程序主动取消除外）。</summary>
    public async Task<FetchResult> FetchAsync(string? apiKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return FetchResult.Fail(FetchFailureKind.NotConfigured,
                "尚未设置 API 密钥，请右键托盘图标选择「设置密钥」。");

        using var request = new HttpRequestMessage(HttpMethod.Get, DefaultEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return FetchResult.Fail(FetchFailureKind.NetworkError, "连接超时（15 秒内未收到响应），请检查网络。");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            return FetchResult.Fail(FetchFailureKind.NetworkError, $"网络连接失败：{ex.Message}");
        }
        catch (Exception ex)
        {
            return FetchResult.Fail(FetchFailureKind.NetworkError, $"请求出现异常：{ex.GetType().Name} {ex.Message}");
        }

        using (response)
        {
            string body;
            try
            {
                body = await response.Content.ReadAsStringAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return FetchResult.Fail(FetchFailureKind.NetworkError, "读取响应超时，请检查网络。");
            }

            return Classify(response.StatusCode, body);
        }
    }

    /// <summary>按状态码归类接口结果（公开静态以便测试）。</summary>
    public static FetchResult Classify(HttpStatusCode statusCode, string body)
    {
        var detail = TryExtractServerMessage(body);
        return statusCode switch
        {
            HttpStatusCode.OK => ParseSuccessBody(body),
            HttpStatusCode.Unauthorized => FetchResult.Fail(FetchFailureKind.InvalidKey,
                "API 密钥无效或已失效（HTTP 401），请重新设置密钥。", detail),
            HttpStatusCode.Forbidden => FetchResult.Fail(FetchFailureKind.NoSubscription,
                "无权访问该接口（HTTP 403），可能尚未订阅 OpenCode Go 或权限不足。", detail),
            HttpStatusCode.PaymentRequired => FetchResult.Fail(FetchFailureKind.NoSubscription,
                "订阅不可用（HTTP 402），请确认 OpenCode Go 订阅状态。", detail),
            HttpStatusCode.TooManyRequests => FetchResult.Fail(FetchFailureKind.RateLimited,
                "请求过于频繁，已被限流（HTTP 429），请稍后再试。", detail),
            var code when (int)code >= 500 => FetchResult.Fail(FetchFailureKind.ServerError,
                $"OpenCode 服务端异常（HTTP {(int)code}），请稍后重试。", detail),
            var code => FetchResult.Fail(FetchFailureKind.ServerError,
                $"接口返回异常状态码（HTTP {(int)code}）。", detail),
        };
    }

    /// <summary>
    /// 解析 HTTP 200 的响应体（公开静态以便测试）。仅读取真实返回的 usage.rolling / weekly / monthly
    /// 中的 percent、status、resetsAt 字段；剩余百分比 = 100 - 已用百分比。不推断任何 Token 或请求次数额度。
    /// </summary>
    public static FetchResult ParseSuccessBody(string body)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "null" : body);
        }
        catch (JsonException)
        {
            return FetchResult.Fail(FetchFailureKind.ParseError, "接口返回内容不是有效的 JSON。", Snip(body));
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("usage", out var usage)
                || usage.ValueKind != JsonValueKind.Object)
            {
                return FetchResult.Fail(FetchFailureKind.ParseError,
                    "接口返回格式不符合预期（缺少 usage 字段）。", Snip(body));
            }

            var rolling = ParseWindow(usage, "rolling");
            var weekly = ParseWindow(usage, "weekly");
            var monthly = ParseWindow(usage, "monthly");
            if (rolling is null && weekly is null && monthly is null)
            {
                return FetchResult.Fail(FetchFailureKind.ParseError,
                    "接口返回中缺少 rolling/weekly/monthly 用量数据，可能当前无订阅，或接口格式已变更。", Snip(body));
            }

            return FetchResult.Ok(new UsageData { Rolling = rolling, Weekly = weekly, Monthly = monthly });
        }
    }

    private static UsageWindow? ParseWindow(JsonElement usage, string name)
    {
        if (!usage.TryGetProperty(name, out var w) || w.ValueKind != JsonValueKind.Object)
            return null;

        double percent = double.NaN;
        string? status = null;
        DateTimeOffset? resetsAt = null;

        if (w.TryGetProperty("percent", out var p))
        {
            if (p.ValueKind == JsonValueKind.Number && p.TryGetDouble(out var d))
                percent = d;
            else if (p.ValueKind == JsonValueKind.String
                     && double.TryParse(p.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var ds))
                percent = ds;
        }

        if (w.TryGetProperty("status", out var s) && s.ValueKind == JsonValueKind.String)
            status = s.GetString();

        if (w.TryGetProperty("resetsAt", out var r))
        {
            if (r.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(r.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto))
                resetsAt = dto;
            else if (r.ValueKind == JsonValueKind.Number && r.TryGetInt64(out var unix))
                resetsAt = DateTimeOffset.FromUnixTimeSeconds(unix);
        }

        return new UsageWindow { PercentUsed = percent, Status = status, ResetsAt = resetsAt };
    }

    /// <summary>从错误响应中提取 error.message。实测错误体形如 {"type":"error","error":{"type":"AuthError","message":"..."}}。</summary>
    private static string? TryExtractServerMessage(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("error", out var err)
                && err.ValueKind == JsonValueKind.Object
                && err.TryGetProperty("message", out var msg)
                && msg.ValueKind == JsonValueKind.String)
            {
                return msg.GetString();
            }
        }
        catch (JsonException)
        {
            // 非 JSON 错误体，忽略
        }

        return null;
    }

    private static string? Snip(string body)
        => string.IsNullOrWhiteSpace(body) ? null
            : body.Length <= 200 ? body
            : body[..200] + "…";
}
