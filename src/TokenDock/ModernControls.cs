using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace TokenDock;

/// <summary>纯白圆角卡片（统一 12px 圆角 + 浅灰细边框），也可作为圆角输入框/内衬盒。</summary>
internal class CardPanel : Panel
{
    private Color? _borderOverride;
    private Func<Color>? _borderSource;

    public CardPanel(float radius = 12f, bool filled = true)
    {
        Radius = radius;
        Filled = filled;
        SetStyle(
            ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor
            | ControlStyles.UserPaint,
            true);
        BackColor = Color.Transparent;
        UiTheme.Follow(this);
    }

    public float Radius { get; }

    /// <summary>false 时只画浅灰底不描边（明细内衬盒用）。</summary>
    public bool Filled { get; }

    /// <summary>固定边框色（优先级高于 BorderSource）。</summary>
    public Color Border
    {
        get => _borderOverride ?? _borderSource?.Invoke() ?? UiTheme.CardBorder;
        set
        {
            _borderOverride = value;
            _borderSource = null;
            Invalidate();
        }
    }

    /// <summary>动态边框色（如输入框：平时用输入框描边、聚焦时用绿色），随主题自动重取。</summary>
    public Func<Color>? BorderSource
    {
        get => _borderSource;
        set
        {
            _borderSource = value;
            _borderOverride = null;
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = UiTheme.RoundedRect(new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f), Radius);
        using (var fill = new SolidBrush(Filled ? UiTheme.Card : UiTheme.Inset))
            g.FillPath(fill, path);
        if (Filled)
        {
            using (var pen = new Pen(Border))
                g.DrawPath(pen, path);
        }
        base.OnPaint(e);
    }
}

/// <summary>统一状态样式：彩色圆点 + 文本（正常/即将重置=绿，已过期=灰，获取失败=红，刷新中=灰）。</summary>
internal sealed class StatusChip : Control
{
    private Color _dot = UiTheme.Green;

    public StatusChip()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor
            | ControlStyles.UserPaint,
            true);
        BackColor = Color.Transparent;
        Font = UiTheme.Body;
        UiTheme.Bind(this, () => UiTheme.TextSecondary);
        UiTheme.Follow(this);
    }

    public void Set(string text, Color dot)
    {
        _dot = dot;
        Text = text;
        var size = TextRenderer.MeasureText(text, Font);
        ClientSize = new Size(UiTheme.Px(18) + size.Width, UiTheme.Px(24));
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var d = UiTheme.Px(8);
        using (var brush = new SolidBrush(_dot))
            g.FillEllipse(brush, 0, Height / 2f - d / 2f, d, d);
        TextRenderer.DrawText(g, Text, Font,
            new Rectangle(UiTheme.Px(14), 0, Width - UiTheme.Px(14), Height), ForeColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }
}

/// <summary>小型加载指示（旋转弧线），刷新中显示在状态文字旁，不遮挡窗口。</summary>
internal sealed class Spinner : Control
{
    private readonly System.Windows.Forms.Timer _timer;
    private float _angle;

    public Spinner()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor
            | ControlStyles.UserPaint,
            true);
        BackColor = Color.Transparent;
        Visible = false;
        _timer = new System.Windows.Forms.Timer { Interval = 80 };
        _timer.Tick += (_, _) =>
        {
            _angle = (_angle + 40f) % 360f;
            Invalidate();
        };
        VisibleChanged += (_, _) =>
        {
            if (Visible) _timer.Start();
            else _timer.Stop();
        };
        UiTheme.Follow(this);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _timer.Dispose();
        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(UiTheme.Gray, UiTheme.Px(2f));
        g.DrawArc(pen, UiTheme.Px(2f), UiTheme.Px(2f), Width - UiTheme.Px(4f), Height - UiTheme.Px(4f), _angle, 110f);
    }
}

/// <summary>细进度条（6px，单一绿色，轻微缓动动画）。</summary>
internal sealed class FlatProgressBar : Control
{
    private double _displayed;
    private double _target;
    private Color _fill = UiTheme.Green;
    private System.Windows.Forms.Timer? _anim;

    public FlatProgressBar()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor
            | ControlStyles.UserPaint,
            true);
        BackColor = Color.Transparent;
        UiTheme.Follow(this);
    }

    public void Set(double value, Color fill)
    {
        _target = Math.Clamp(value, 0, 100);
        _fill = fill;
        if (!UiTheme.Animations)
        {
            // 关掉动画：直接到位，不启动缓动计时器
            _anim?.Stop();
            _displayed = _target;
            Invalidate();
            return;
        }

        if (_anim is null)
        {
            _anim = new System.Windows.Forms.Timer { Interval = 15 };
            _anim.Tick += (_, _) => Step();
        }
        _anim.Start();
    }

    private void Step()
    {
        var diff = _target - _displayed;
        if (Math.Abs(diff) < 0.5)
        {
            _displayed = _target;
            _anim!.Stop();
            Invalidate();
            return;
        }
        _displayed += diff * 0.28;
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _anim?.Dispose();
        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
        var radius = Height / 2f;

        using (var track = UiTheme.RoundedRect(rect, radius))
        {
            using var brush = new SolidBrush(UiTheme.Track);
            g.FillPath(brush, track);
        }

        if (_displayed <= 0) return;

        var width = (float)(rect.Width * _displayed / 100.0);
        if (width < radius * 2) width = radius * 2;
        if (width > rect.Width) width = rect.Width;
        using (var path = UiTheme.RoundedRect(new RectangleF(rect.X, rect.Y, width, rect.Height), radius))
        {
            using var brush = new SolidBrush(_fill);
            g.FillPath(brush, path);
        }
    }
}

/// <summary>大号余量数字 + 小号百分号，底对齐（数字与 % 无下延笔画，底对齐即同基线）。</summary>
internal sealed class PercentText : Control
{
    private string _number = "—";
    private string _sign = "%";
    private Color _color = UiTheme.Green;
    private bool _chinese;

    public PercentText()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor
            | ControlStyles.UserPaint,
            true);
        BackColor = Color.Transparent;
    }

    public void Set(string number, string sign, Color color)
    {
        _number = number;
        _sign = sign;
        _color = color;
        _chinese = false;
        Invalidate();
    }

    public void SetText(string text, Color color)
    {
        _number = text;
        _sign = string.Empty;
        _color = color;
        _chinese = true;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        if (_chinese)
        {
            TextRenderer.DrawText(g, _number, UiTheme.Body, ClientRectangle, _color,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            return;
        }

        var numberSize = TextRenderer.MeasureText(_number, UiTheme.Percent);
        var signSize = string.IsNullOrEmpty(_sign) ? Size.Empty : TextRenderer.MeasureText(_sign, UiTheme.Body);
        var total = numberSize.Width + signSize.Width + UiTheme.Px(2);
        var x = Width - total;

        TextRenderer.DrawText(g, _number, UiTheme.Percent,
            new Rectangle(x, 0, numberSize.Width, Height), _color,
            TextFormatFlags.Right | TextFormatFlags.Bottom | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        if (signSize != Size.Empty)
        {
            TextRenderer.DrawText(g, _sign, UiTheme.Body,
                new Rectangle(x + numberSize.Width + UiTheme.Px(2), 0, signSize.Width + UiTheme.Px(4), Height), _color,
                TextFormatFlags.Left | TextFormatFlags.Bottom | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        }
    }
}

/// <summary>统一按钮：Primary 绿色实心（唯一主按钮），Secondary 白底描边；悬停/按压/禁用态完整。</summary>
internal sealed class PillButton : Control, IButtonControl
{
    private readonly bool _primary;
    private bool _hover;
    private bool _down;

    public PillButton(bool primary = false)
    {
        _primary = primary;
        SetStyle(
            ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor
            | ControlStyles.UserPaint | ControlStyles.StandardClick | ControlStyles.StandardDoubleClick,
            true);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        Font = UiTheme.Body;
        UiTheme.Follow(this);
    }

    public DialogResult DialogResult { get; set; }

    public void NotifyDefault(bool value)
    {
    }

    void IButtonControl.PerformClick() => OnClick(EventArgs.Empty);

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
        using var path = UiTheme.RoundedRect(rect, UiTheme.Px(12f));

        Color back, border, text;
        if (!Enabled)
        {
            back = UiTheme.DisabledBack;
            border = UiTheme.Track;
            text = UiTheme.DisabledText;
        }
        else if (_primary)
        {
            back = _down ? UiTheme.GreenPress : _hover ? UiTheme.GreenHover : UiTheme.Green;
            border = back;
            text = Color.White;
        }
        else
        {
            back = _down ? UiTheme.SecondaryBtnPress : _hover ? UiTheme.SecondaryBtnHover : UiTheme.SecondaryBtn;
            border = UiTheme.SecondaryBtnBorder;
            text = UiTheme.TextPrimary;
        }

        using (var brush = new SolidBrush(back))
            g.FillPath(brush, path);
        using (var pen = new Pen(border))
            g.DrawPath(pen, path);
        TextRenderer.DrawText(g, Text, Font, new Rectangle(0, 0, Width, Height), text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }
}

/// <summary>灰色文字按钮（显示/隐藏、模型用量明细等次级操作）。</summary>
internal sealed class LinkButton : Control
{
    private bool _hover;

    public LinkButton()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor
            | ControlStyles.UserPaint | ControlStyles.StandardClick,
            true);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        Font = UiTheme.Tiny;
        UiTheme.Bind(this, () => UiTheme.TextSecondary);
        UiTheme.Follow(this);
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle,
            _hover ? UiTheme.TextPrimary : UiTheme.TextSecondary,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }
}

/// <summary>方形图标按钮（设置入口等）：透明底，悬停浅灰圆角底。</summary>
internal sealed class IconButton : Control
{
    private bool _hover;

    public IconButton(string glyph)
    {
        Text = glyph;
        SetStyle(
            ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor
            | ControlStyles.UserPaint | ControlStyles.StandardClick,
            true);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        Font = UiTheme.Icon;
        UiTheme.Bind(this, () => UiTheme.TextSecondary);
        UiTheme.Follow(this);
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (_hover)
        {
            using var path = UiTheme.RoundedRect(new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f), UiTheme.Px(8f));
            using var brush = new SolidBrush(UiTheme.SecondaryBtnHover);
            g.FillPath(brush, path);
        }
        TextRenderer.DrawText(g, Text, Font, new Rectangle(0, 0, Width, Height), ForeColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }
}
