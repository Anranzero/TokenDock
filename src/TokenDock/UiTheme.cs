using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace TokenDock;

/// <summary>
/// Win11 专业工具风主题：浅色 / 暗色 两套调色板 + 毛玻璃微调（更细边框、更轻阴影）。
/// 单一绿色主色、中性灰阶、8px 栅格；像素字体按缩放系数生成（100%/125%/150% 确定性布局）。
/// 使用前必须调用 Init(scale)；调色板由 Appearance 控制器切换（切换后自动通知控件重绘）。
/// </summary>
internal static class UiTheme
{
    public static float Scale { get; private set; } = 1f;

    /// <summary>把 100% 基准设计像素换算为实际像素（8px 栅格随缩放整体平移）。</summary>
    public static int Px(double value) => (int)Math.Round(value * Scale);

    // ---- 调色板切换（由 Appearance 调用；切换会触发 Changed，控件据此重绘） ----
    public static bool IsDark { get; private set; }
    public static bool IsGlass { get; private set; }

    /// <summary>界面动画开关（进度条缓动等）。</summary>
    public static bool Animations { get; set; } = true;

    /// <summary>调色板变化通知：自绘控件订阅后重绘，颜色绑定（Bind/BindBack）自动重取。</summary>
    public static event Action? Changed;

    private static Palette _p = Palette.For(dark: false, glass: false);

    public static void ApplyPalette(bool dark, bool glass)
    {
        IsDark = dark;
        IsGlass = glass;
        _p = Palette.For(dark, glass);
        Changed?.Invoke();
    }

    /// <summary>把控件的 ForeColor 绑定到主题角色：立即生效，主题切换时自动重取（控件释放时自动解绑）。</summary>
    public static void Bind(Control control, Func<Color> fore)
    {
        void Apply()
        {
            if (!control.IsDisposed) control.ForeColor = fore();
        }

        Apply();
        Changed += Apply;
        control.Disposed += (_, _) => Changed -= Apply;
    }

    /// <summary>把控件的 BackColor 绑定到主题角色（同上）。</summary>
    public static void BindBack(Control control, Func<Color> back)
    {
        void Apply()
        {
            if (!control.IsDisposed) control.BackColor = back();
        }

        Apply();
        Changed += Apply;
        control.Disposed += (_, _) => Changed -= Apply;
    }

    /// <summary>自绘控件订阅主题变化 → 重绘（释放时自动解绑）。</summary>
    public static void Follow(Control control)
    {
        void Apply()
        {
            if (!control.IsDisposed) control.Invalidate();
        }

        Changed += Apply;
        control.Disposed += (_, _) => Changed -= Apply;
    }

    // ---- 颜色角色（读取当前调色板；自绘控件每次绘制时读取即可自动适配） ----
    public static Color Canvas => _p.Canvas;
    public static Color Card => _p.Card;
    public static Color CardBorder => _p.CardBorder;
    public static Color InputBorder => _p.InputBorder;
    public static Color Inset => _p.Inset;
    public static Color Hairline => _p.Hairline;
    public static Color TextPrimary => _p.TextPrimary;
    public static Color TextSecondary => _p.TextSecondary;
    public static Color Green => _p.Green;
    public static Color GreenHover => _p.GreenHover;
    public static Color GreenPress => _p.GreenPress;
    public static Color Red => _p.Red;
    public static Color Gray => _p.Gray;
    public static Color Track => _p.Track;
    public static Color SegmentTrack => _p.SegmentTrack;
    public static Color SecondaryBtn => _p.SecondaryBtn;
    public static Color SecondaryBtnBorder => _p.SecondaryBtnBorder;
    public static Color SecondaryBtnHover => _p.SecondaryBtnHover;
    public static Color SecondaryBtnPress => _p.SecondaryBtnPress;
    public static Color DisabledBack => _p.DisabledBack;
    public static Color DisabledText => _p.DisabledText;

    // ---- 字体：三档字号 + 百分比展示数字（像素尺寸 × 缩放系数） ----
    public static Font Title { get; private set; } = null!;   // 15px 粗：窗口内区块标题
    public static Font Body { get; private set; } = null!;    // 12px：正文/按钮
    public static Font Tiny { get; private set; } = null!;    // 11px：辅助信息
    public static Font Percent { get; private set; } = null!; // 26px：百分比展示数字（Segoe UI）
    public static Font Icon { get; private set; } = null!;    // 13px：Segoe MDL2 图标

    public static void Init(float scale)
    {
        Scale = scale;
        Title = YaHei(15f, bold: true);
        Body = YaHei(12f);
        Tiny = YaHei(11f);
        Percent = Segoe(26f, bold: true);
        Icon = IconFont(13f);
    }

    private static Font YaHei(float px, bool bold = false)
        => new("Microsoft YaHei UI", px * Scale, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);

    private static Font Segoe(float px, bool bold = false)
        => new("Segoe UI", px * Scale, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);

    private static Font IconFont(float px)
    {
        try
        {
            return new Font("Segoe MDL2 Assets", px * Scale, FontStyle.Regular, GraphicsUnit.Pixel);
        }
        catch (ArgumentException)
        {
            return new Font("Segoe UI Symbol", px * Scale, FontStyle.Regular, GraphicsUnit.Pixel);
        }
    }

    public static Color LevelColor(double? remaining)
    {
        if (remaining is null) return Gray;
        if (remaining.Value >= 50) return Green;
        if (remaining.Value >= 20) return Green; // 主色只用一种绿，低余量靠状态行提示
        return Green;
    }

    public static GraphicsPath RoundedRect(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        var d = radius * 2;
        if (d <= 0 || bounds.Width < d || bounds.Height < d)
        {
            path.AddRectangle(bounds);
            return path;
        }

        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>克制阴影：单层极淡（8/255；毛玻璃下更轻，取一半）。</summary>
    public static void DrawSoftShadow(Graphics g, RectangleF cardRect, float radius)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = RoundedRect(
            new RectangleF(cardRect.X, cardRect.Y + 1.5f, cardRect.Width, cardRect.Height + 2f), radius);
        using var brush = new SolidBrush(Color.FromArgb(IsGlass ? 4 : 8, 15, 23, 42));
        g.FillPath(brush, path);
    }

    /// <summary>WCAG 相对亮度对比度（1~21）。自检用于保证暗色/浅色下文字可读。</summary>
    public static double ContrastRatio(Color a, Color b)
    {
        var la = RelativeLuminance(a);
        var lb = RelativeLuminance(b);
        var (hi, lo) = la >= lb ? (la, lb) : (lb, la);
        return (hi + 0.05) / (lo + 0.05);
    }

    private static double RelativeLuminance(Color color)
    {
        static double Channel(int value)
        {
            var c = value / 255.0;
            return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
    }

    /// <summary>取指定主题 / 效果下的调色板（自检与单测用；不改变当前主题）。</summary>
    internal static Palette PaletteFor(bool dark, bool glass) => Palette.For(dark, glass);

    /// <summary>浅色 / 暗色两套调色板；毛玻璃模式使用更细的边框（半透明灰/白）。</summary>
    internal sealed record Palette(
        Color Canvas, Color Card, Color CardBorder, Color InputBorder, Color Inset, Color Hairline,
        Color TextPrimary, Color TextSecondary, Color Green, Color GreenHover, Color GreenPress,
        Color Red, Color Gray, Color Track, Color SegmentTrack,
        Color SecondaryBtn, Color SecondaryBtnBorder, Color SecondaryBtnHover, Color SecondaryBtnPress,
        Color DisabledBack, Color DisabledText)
    {
        public static Palette For(bool dark, bool glass) => dark
            ? new Palette(
                Canvas: Rgb(30, 31, 34),
                Card: Rgb(40, 41, 45),
                // 毛玻璃：边框更细更淡（半透明白，叠在卡片底色上形成柔和高光边）
                CardBorder: glass ? Argb(70, 255, 255, 255) : Rgb(60, 63, 68),
                InputBorder: glass ? Argb(96, 255, 255, 255) : Rgb(76, 79, 85),
                Inset: Rgb(34, 35, 38),
                Hairline: Rgb(56, 58, 63),
                TextPrimary: Rgb(237, 239, 243),
                TextSecondary: Rgb(152, 157, 165),
                Green: Rgb(34, 168, 94),
                GreenHover: Rgb(46, 186, 108),
                GreenPress: Rgb(26, 140, 78),
                Red: Rgb(248, 113, 113),
                Gray: Rgb(128, 134, 142),
                Track: Rgb(58, 61, 66),
                // 暗色下轨道比卡片更暗，选中段（卡片色）才会显得更亮（Win11 惯例）
                SegmentTrack: Rgb(33, 34, 38),
                SecondaryBtn: Rgb(46, 48, 52),
                SecondaryBtnBorder: Rgb(70, 73, 79),
                SecondaryBtnHover: Rgb(56, 59, 64),
                SecondaryBtnPress: Rgb(66, 69, 74),
                DisabledBack: Rgb(46, 48, 52),
                DisabledText: Rgb(122, 128, 136))
            : new Palette(
                Canvas: Rgb(243, 244, 246),
                Card: Color.White,
                CardBorder: glass ? Argb(90, 107, 114, 128) : Rgb(228, 231, 236),
                InputBorder: glass ? Argb(120, 107, 114, 128) : Rgb(209, 213, 219),
                Inset: Rgb(249, 250, 251),
                Hairline: Rgb(228, 231, 236),
                TextPrimary: Rgb(17, 24, 39),
                TextSecondary: Rgb(107, 114, 128),
                Green: Rgb(22, 163, 74),
                GreenHover: Rgb(21, 128, 61),
                GreenPress: Rgb(22, 101, 52),
                Red: Rgb(220, 38, 38),
                Gray: Rgb(156, 163, 175),
                Track: Rgb(229, 231, 235),
                SegmentTrack: Rgb(238, 240, 243),
                SecondaryBtn: Color.White,
                SecondaryBtnBorder: Rgb(209, 213, 219),
                SecondaryBtnHover: Rgb(243, 244, 246),
                SecondaryBtnPress: Rgb(229, 231, 235),
                DisabledBack: Rgb(243, 244, 246),
                DisabledText: Rgb(156, 163, 175));

        private static Color Rgb(int r, int g, int b) => Color.FromArgb(255, r, g, b);

        private static Color Argb(int a, int r, int g, int b) => Color.FromArgb(a, r, g, b);
    }
}
