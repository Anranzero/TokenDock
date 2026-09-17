using Xunit;

namespace TokenDock.Tests;

public class DisplayFormatTests
{
    [Theory]
    [InlineData(null, "未知")]
    [InlineData(17.5, "18%")]
    [InlineData(0.0, "0%")]
    [InlineData(100.0, "100%")]
    public void FormatRemainingPercent_Basics(double? remaining, string expected)
        => Assert.Equal(expected, DisplayFormat.FormatRemainingPercent(remaining));

    [Theory]
    [InlineData(null, "状态未知")]
    [InlineData("ok", "正常")]
    [InlineData("warning", "偏高")]
    [InlineData("limited", "已限流")]
    [InlineData("weird-status", "weird-status")]
    public void FormatStatus_MapsKnownStatuses(string? status, string expected)
        => Assert.Equal(expected, DisplayFormat.FormatStatus(status));

    [Fact]
    public void FormatCountdown_Missing_ReturnsUnknown()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Equal("重置时间未知", DisplayFormat.FormatCountdown(null, now));
    }

    [Fact]
    public void FormatCountdown_90Minutes()
    {
        var now = new DateTimeOffset(2026, 9, 17, 8, 0, 0, TimeSpan.Zero);
        Assert.Equal("1小时30分钟后重置", DisplayFormat.FormatCountdown(now.AddMinutes(90), now));
    }

    [Fact]
    public void FormatCountdown_MultiDays()
    {
        var now = new DateTimeOffset(2026, 9, 17, 8, 0, 0, TimeSpan.Zero);
        Assert.Equal("2天5小时后重置", DisplayFormat.FormatCountdown(now.AddHours(53), now));
    }

    [Fact]
    public void FormatCountdown_WholeHours_OmitsZeroMinutes()
    {
        var now = new DateTimeOffset(2026, 9, 17, 8, 0, 0, TimeSpan.Zero);
        Assert.Equal("5小时后重置", DisplayFormat.FormatCountdown(now.AddHours(5), now));
        Assert.Equal("7天后重置", DisplayFormat.FormatCountdown(now.AddDays(7), now));
    }

    [Fact]
    public void FormatCountdown_Passed_WaitsForRefresh()
    {
        var now = new DateTimeOffset(2026, 9, 17, 8, 0, 0, TimeSpan.Zero);
        Assert.Contains("已到重置时间", DisplayFormat.FormatCountdown(now.AddMinutes(-1), now));
    }
}
