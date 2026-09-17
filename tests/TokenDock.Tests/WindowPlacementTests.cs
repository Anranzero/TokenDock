using Xunit;

namespace TokenDock.Tests;

/// <summary>窗口位置收敛（展开明细后不被任务栏遮挡）的纯函数测试。</summary>
public class WindowPlacementTests
{
    private static readonly Rectangle WorkArea = new(0, 0, 1920, 1040);

    [Fact]
    public void AnchorBottom_Expand_KeepsBottomEdge()
    {
        // 贴底窗口展开：底边保持 1000 不动，窗口向上长
        var result = WindowPlacement.AnchorBottom(new Point(1460, 700), new Size(440, 300), new Size(440, 500), WorkArea);

        Assert.Equal(new Point(1460, 500), result);
        Assert.Equal(1000, result.Y + 500);
    }

    [Fact]
    public void AnchorBottom_Collapse_DoesNotFloat()
    {
        // 展开态贴底（底边 1040）收起后仍贴底，不会浮在半空
        var result = WindowPlacement.AnchorBottom(new Point(1460, 540), new Size(440, 500), new Size(440, 300), WorkArea);

        Assert.Equal(1040, result.Y + 300);
    }

    [Fact]
    public void AnchorBottom_Overflow_ClampsIntoWorkArea()
    {
        // 底边已在工作区外时，先锚定再收敛
        var result = WindowPlacement.AnchorBottom(new Point(1460, 900), new Size(440, 300), new Size(440, 500), WorkArea);

        Assert.True(result.Y + 500 <= WorkArea.Bottom);
    }

    [Fact]
    public void Clamp_BottomOverflow_MovesWindowUp()
    {
        // 窗口贴在右下角，展开后高度增加导致底边越界 → 上收到工作区底边（1040-500=540）
        var result = WindowPlacement.Clamp(new Point(1460, 900), new Size(440, 500), WorkArea);

        Assert.Equal(new Point(1460, 540), result);
        Assert.True(result.Y + 500 <= WorkArea.Bottom);
    }

    [Fact]
    public void Clamp_RightOverflow_MovesWindowLeft()
    {
        var result = WindowPlacement.Clamp(new Point(1800, 100), new Size(440, 500), WorkArea);

        Assert.True(result.X + 440 <= WorkArea.Right);
        Assert.Equal(100, result.Y);
    }

    [Fact]
    public void Clamp_Inside_Unchanged()
    {
        var location = new Point(200, 200);
        Assert.Equal(location, WindowPlacement.Clamp(location, new Size(440, 500), WorkArea));
    }

    [Fact]
    public void Clamp_AboveTop_ClampedToTop()
    {
        var result = WindowPlacement.Clamp(new Point(-50, -80), new Size(440, 500), WorkArea);

        Assert.Equal(0, result.X);
        Assert.Equal(0, result.Y);
    }
}
