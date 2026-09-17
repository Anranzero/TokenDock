using System.Net;
using Xunit;

namespace TokenDock.Tests;

public class UsageApiClientTests
{
    [Fact]
    public void Parse_NormalResponse_ComputesRemainingAs100MinusUsed()
    {
        var result = UsageApiClient.ParseSuccessBody(
            "{\"usage\":{\"rolling\":{\"percent\":82.5,\"status\":\"ok\",\"resetsAt\":\"2026-09-17T12:00:00Z\"},"
            + "\"weekly\":{\"percent\":10},\"monthly\":{\"percent\":3}}}");

        Assert.True(result.Success);
        Assert.Equal(17.5, result.Data!.Rolling!.RemainingPercent);
        Assert.Equal(90, result.Data.Weekly!.RemainingPercent);
        Assert.Equal(97, result.Data.Monthly!.RemainingPercent);
    }

    [Fact]
    public void Parse_ResetsAtIso_Parsed()
    {
        var result = UsageApiClient.ParseSuccessBody(
            "{\"usage\":{\"rolling\":{\"percent\":1,\"resetsAt\":\"2026-09-17T12:00:00Z\"}}}");

        Assert.Equal(DateTimeOffset.Parse("2026-09-17T12:00:00Z"), result.Data!.Rolling!.ResetsAt);
    }

    [Fact]
    public void Parse_UnixSecondsResetsAt_Parsed()
    {
        var result = UsageApiClient.ParseSuccessBody(
            "{\"usage\":{\"rolling\":{\"percent\":1,\"resetsAt\":1758096000}}}");

        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1758096000), result.Data!.Rolling!.ResetsAt);
    }

    [Fact]
    public void Parse_StringPercent_Tolerated()
    {
        var result = UsageApiClient.ParseSuccessBody("{\"usage\":{\"rolling\":{\"percent\":\"55.5\"}}}");

        Assert.True(result.Success);
        Assert.Equal(44.5, result.Data!.Rolling!.RemainingPercent);
    }

    [Fact]
    public void Parse_PercentOver100_ClampsRemainingToZero()
    {
        var result = UsageApiClient.ParseSuccessBody("{\"usage\":{\"rolling\":{\"percent\":120}}}");

        Assert.True(result.Success);
        Assert.Equal(0, result.Data!.Rolling!.RemainingPercent);
    }

    [Fact]
    public void Parse_MissingUsageField_FailsWithParseError()
        => Assert.Equal(FetchFailureKind.ParseError, UsageApiClient.ParseSuccessBody("{\"foo\":1}").FailureKind);

    [Fact]
    public void Parse_EmptyUsageObject_FailsWithParseError()
        => Assert.Equal(FetchFailureKind.ParseError, UsageApiClient.ParseSuccessBody("{\"usage\":{}}").FailureKind);

    [Fact]
    public void Parse_InvalidJson_FailsWithParseError()
        => Assert.Equal(FetchFailureKind.ParseError, UsageApiClient.ParseSuccessBody("<html>500</html>").FailureKind);

    [Fact]
    public void Classify_401_InvalidKeyWithServerMessage()
    {
        var result = UsageApiClient.Classify(HttpStatusCode.Unauthorized,
            "{\"type\":\"error\",\"error\":{\"type\":\"AuthError\",\"message\":\"Unauthorized\"}}");

        Assert.Equal(FetchFailureKind.InvalidKey, result.FailureKind);
        Assert.Equal("Unauthorized", result.ServerDetail);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, FetchFailureKind.NoSubscription)]
    [InlineData(HttpStatusCode.PaymentRequired, FetchFailureKind.NoSubscription)]
    [InlineData(HttpStatusCode.TooManyRequests, FetchFailureKind.RateLimited)]
    [InlineData(HttpStatusCode.InternalServerError, FetchFailureKind.ServerError)]
    [InlineData(HttpStatusCode.NotFound, FetchFailureKind.ServerError)]
    public void Classify_StatusCodes(HttpStatusCode code, FetchFailureKind expected)
        => Assert.Equal(expected, UsageApiClient.Classify(code, "{}").FailureKind);
}
