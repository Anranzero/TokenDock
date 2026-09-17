using System.Net;
using System.Text;
using Xunit;

namespace TokenDock.Tests;

/// <summary>覆盖 FetchAsync 的网络层行为与异常映射（自定义 HttpMessageHandler，不发真实请求）。</summary>
public class FetchAsyncTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        private readonly Func<HttpResponseMessage> _respond;

        public StubHandler(Func<HttpResponseMessage> respond) => _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(_respond());
        }
    }

    private static (UsageApiClient Client, StubHandler Handler) Create(Func<HttpResponseMessage> respond)
    {
        var handler = new StubHandler(respond);
        return (new UsageApiClient(new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan }), handler);
    }

    [Fact]
    public async Task FetchAsync_MissingKey_ReturnsNotConfiguredWithoutRequest()
    {
        var (client, handler) = Create(() => throw new InvalidOperationException("未配置密钥时不应发起请求"));

        var result = await client.FetchAsync(null);

        Assert.Equal(FetchFailureKind.NotConfigured, result.FailureKind);
        Assert.Null(handler.LastRequest);
    }

    [Fact]
    public async Task FetchAsync_Http200_ParsesUsageAndSendsBearerHeader()
    {
        var (client, handler) = Create(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"usage\":{\"rolling\":{\"percent\":40,\"status\":\"ok\"}}}", Encoding.UTF8, "application/json"),
        });

        var result = await client.FetchAsync("test-key");

        Assert.True(result.Success);
        Assert.Equal(60, result.Data!.Rolling!.RemainingPercent);
        Assert.Equal("Bearer", handler.LastRequest!.Headers.Authorization!.Scheme);
        Assert.Equal("test-key", handler.LastRequest.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task FetchAsync_Http401_ClassifiedAsInvalidKey()
    {
        var (client, _) = Create(() => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent(
                "{\"type\":\"error\",\"error\":{\"type\":\"AuthError\",\"message\":\"Unauthorized\"}}"),
        });

        var result = await client.FetchAsync("bad-key");

        Assert.Equal(FetchFailureKind.InvalidKey, result.FailureKind);
        Assert.Equal("Unauthorized", result.ServerDetail);
    }

    [Fact]
    public async Task FetchAsync_NetworkFailure_ReturnsNetworkError()
    {
        var (client, _) = Create(() => throw new HttpRequestException("connection refused"));

        var result = await client.FetchAsync("k");

        Assert.Equal(FetchFailureKind.NetworkError, result.FailureKind);
        Assert.Contains("网络连接失败", result.ErrorMessage);
    }

    [Fact]
    public async Task FetchAsync_CancellationWithoutUserCancel_TreatedAsTimeout()
    {
        var (client, _) = Create(() => throw new TaskCanceledException());

        var result = await client.FetchAsync("k");

        Assert.Equal(FetchFailureKind.NetworkError, result.FailureKind);
        Assert.Contains("超时", result.ErrorMessage);
    }
}
