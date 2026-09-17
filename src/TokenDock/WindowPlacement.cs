using System.Drawing;

namespace TokenDock;

/// <summary>
/// 窗口位置收敛（纯函数，便于测试）：尺寸变化时以底边为锚（展开向上长、收起向下落，
/// 底边不动），并保证整窗不越出工作区，避免展开内容被任务栏遮挡。
/// </summary>
public static class WindowPlacement
{
    /// <summary>保持底边位置不变地应用新尺寸，然后收敛进工作区。</summary>
    public static Point AnchorBottom(Point location, Size oldSize, Size newSize, Rectangle workArea)
    {
        var anchored = new Point(location.X, location.Y + oldSize.Height - newSize.Height);
        return Clamp(anchored, newSize, workArea);
    }

    public static Point Clamp(Point location, Size size, Rectangle workArea)
    {
        var x = location.X;
        var y = location.Y;
        if (location.X + size.Width > workArea.Right) x = workArea.Right - size.Width;
        if (location.Y + size.Height > workArea.Bottom) y = workArea.Bottom - size.Height;
        if (x < workArea.Left) x = workArea.Left;
        if (y < workArea.Top) y = workArea.Top;
        return new Point(x, y);
    }
}
