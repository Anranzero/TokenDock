using System.Net;
using Xunit;

namespace TokenDock.Tests;

public class AppStateTests
{
    private static FetchResult SuccessResult() => UsageApiClient.ParseSuccessBody(
        "{\"usage\":{\"rolling\":{\"percent\":82.5,\"status\":\"ok\"},\"weekly\":{\"percent\":10},\"monthly\":{\"percent\":3}}}");

    [Fact]
    public void Apply_Success_StoresDataAndClearsStale()
    {
        var state = new AppState();
        state.Apply(SuccessResult());

        Assert.False(state.IsStale);
        Assert.NotNull(state.LastGood);
        Assert.Equal(17.5, state.LastGood!.Rolling!.RemainingPercent);
        Assert.Equal(FetchFailureKind.None, state.LastFailureKind);
    }

    [Fact]
    public void Apply_FailureAfterSuccess_KeepsOldDataAndMarksStale()
    {
        var state = new AppState();
        state.Apply(SuccessResult());

        state.Apply(UsageApiClient.Classify(HttpStatusCode.Unauthorized, "{}"));

        Assert.True(state.IsStale);
        Assert.NotNull(state.LastGood);
        // 不能显示为满额：旧数据保留，仍是 17.5%
        Assert.Equal(17.5, state.LastGood!.Rolling!.RemainingPercent);
        Assert.Contains("401", state.StatusText);
        Assert.Contains("最后成功", state.StatusText);
    }

    [Fact]
    public void Apply_FailureWithoutPriorData_ShowsErrorInsteadOfData()
    {
        var state = new AppState();
        state.Apply(FetchResult.Fail(FetchFailureKind.NetworkError, "网络连接失败或超时，请检查网络。"));

        Assert.Null(state.LastGood);
        Assert.False(state.IsStale);
        Assert.Contains("网络连接失败", state.StatusText);
    }

    [Fact]
    public void Apply_SuccessAfterFailure_ClearsStale()
    {
        var state = new AppState();
        state.Apply(FetchResult.Fail(FetchFailureKind.RateLimited, "限流"));
        state.Apply(SuccessResult());

        Assert.False(state.IsStale);
        Assert.Equal(FetchFailureKind.None, state.LastFailureKind);
    }
}
