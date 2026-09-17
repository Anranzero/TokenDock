using Xunit;

namespace TokenDock.Tests;

/// <summary>模型美元合计（保留结构，官方接口尚未提供该数据）的测试。</summary>
public class ModelMoneyTests
{
    [Fact]
    public void TotalUsd_SumsLines()
    {
        var total = ModelUsageMath.TotalUsd(new[]
        {
            new ModelUsageLine { Model = "a", CostUsd = 0.1m },
            new ModelUsageLine { Model = "b", CostUsd = 0.2m },
            new ModelUsageLine { Model = "c", CostUsd = 0.3m },
        });
        Assert.Equal(0.6m, total);
    }
}
