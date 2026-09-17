using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace TokenDock;

/// <summary>
/// 透明度滑杆（自绘，8px 栅格）：左侧说明 + 4px 轨道 + 圆形滑块 + 右侧数值。
/// 取值 50~100（百分比，越小越透明）；支持拖动与方向键微调。
/// </summary>
internal sealed class SliderRow : Control
{
    private int _value = 88;
    private bool _dragging;
    private bool _hover;

    public SliderRow()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor
            | ControlStyles.UserPaint | ControlStyles.StandardClick | ControlStyles.Selectable,
            true);
        BackColor = Color.Transparent;
        Font = UiTheme.Tiny;
        Height = UiTheme.Px(32);
        UiTheme.Follow(this);
    }

    public event Action? ValueChanged;

    /// <summary>透明度百分比（60~100，与 AppearanceSettings.MinOpacity/MaxOpacity 对应）。</summary>
    public int Value
    {
        get => _value;
        set
        {
            var clamped = Math.Clamp(value, 60, 100);
            if (clamped == _value) return;
            _value = clamped;
            Invalidate();
            ValueChanged?.Invoke();
        }
    }

    private RectangleF TrackBounds
    {
        get
        {
            var left = UiTheme.Px(84);
            var right = Width - UiTheme.Px(52);
            var height = UiTheme.Px(4);
            return new RectangleF(left, (Height - height) / 2f, Math.Max(UiTheme.Px(40), right - left), height);
        }
    }

    private float Ratio => (_value - 60) / 40f;

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (!Enabled) return;
        if (e.Button == MouseButtons.Left)
        {
            _dragging = true;
            Focus();
            SetFromX(e.X);
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragging) SetFromX(e.X);
        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _dragging = false;
        base.OnMouseUp(e);
    }

    protected override bool IsInputKey(Keys keyData) => keyData switch
    {
        Keys.Left or Keys.Right or Keys.Home or Keys.End => true,
        _ => base.IsInputKey(keyData),
    };

    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Left: Value -= 1; break;
            case Keys.Right: Value += 1; break;
            case Keys.Home: Value = 60; break;
            case Keys.End: Value = 100; break;
        }
        base.OnKeyDown(e);
    }

    private void SetFromX(int x)
    {
        var track = TrackBounds;
        var ratio = (x - track.Left) / track.Width;
        Value = 60 + (int)Math.Round(Math.Clamp(ratio, 0f, 1f) * 40);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var caption = Enabled ? UiTheme.TextSecondary : UiTheme.DisabledText;
        TextRenderer.DrawText(g, "透明度", Font, new Rectangle(0, 0, UiTheme.Px(78), Height), caption,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);

        var track = TrackBounds;
        using (var path = UiTheme.RoundedRect(track, track.Height / 2f))
        {
            using var brush = new SolidBrush(Enabled ? UiTheme.Track : UiTheme.DisabledBack);
            g.FillPath(brush, path);
        }

        var filledWidth = track.Width * Ratio;
        if (filledWidth > 0)
        {
            using var path = UiTheme.RoundedRect(
                new RectangleF(track.X, track.Y, Math.Max(filledWidth, track.Height), track.Height), track.Height / 2f);
            using var brush = new SolidBrush(Enabled ? UiTheme.Green : UiTheme.DisabledText);
            g.FillPath(brush, path);
        }

        // 圆形滑块：白底 + 细边（毛玻璃下边框更细、悬停略放大）
        var radius = UiTheme.Px(_dragging || _hover ? 8f : 7f);
        var cx = track.X + filledWidth;
        var cy = track.Y + track.Height / 2f;
        var knob = new RectangleF(cx - radius, cy - radius, radius * 2, radius * 2);
        using (var brush = new SolidBrush(UiTheme.IsDark ? UiTheme.SecondaryBtn : Color.White))
            g.FillEllipse(brush, knob);
        using (var pen = new Pen(Enabled ? UiTheme.SecondaryBtnBorder : UiTheme.DisabledBack))
            g.DrawEllipse(pen, knob);

        var valueColor = Enabled ? UiTheme.TextPrimary : UiTheme.DisabledText;
        TextRenderer.DrawText(g, _value + "%", Font, new Rectangle(Width - UiTheme.Px(48), 0, UiTheme.Px(48), Height),
            valueColor, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }
}

/// <summary>开关行（自绘，8px 栅格）：圆角方框 + 绿色勾选 + 说明文字。</summary>
internal sealed class CheckRow : Control
{
    private bool _checked;
    private bool _hover;

    public CheckRow(string text)
    {
        Text = text;
        SetStyle(
            ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor
            | ControlStyles.UserPaint | ControlStyles.StandardClick | ControlStyles.Selectable,
            true);
        BackColor = Color.Transparent;
        Font = UiTheme.Body;
        Cursor = Cursors.Hand;
        Height = UiTheme.Px(32);
        UiTheme.Follow(this);
    }

    public event Action? CheckedChanged;

    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;
            _checked = value;
            Invalidate();
            CheckedChanged?.Invoke();
        }
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (!Enabled) return;
        Focus();
        Checked = !Checked;
        base.OnMouseDown(e);
    }

    protected override bool IsInputKey(Keys keyData) => keyData == Keys.Space || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Space) Checked = !Checked;
        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var box = new RectangleF(UiTheme.Px(2), (Height - UiTheme.Px(16)) / 2f, UiTheme.Px(16), UiTheme.Px(16));
        using (var path = UiTheme.RoundedRect(box, UiTheme.Px(4f)))
        {
            if (_checked)
            {
                using var fill = new SolidBrush(Enabled ? UiTheme.Green : UiTheme.DisabledText);
                g.FillPath(fill, path);
            }
            else if (_hover && Enabled)
            {
                using var fill = new SolidBrush(UiTheme.SecondaryBtnHover);
                g.FillPath(fill, path);
            }

            using var pen = new Pen(_checked
                ? (Enabled ? UiTheme.Green : UiTheme.DisabledText)
                : UiTheme.SecondaryBtnBorder);
            g.DrawPath(pen, path);
        }

        if (_checked)
        {
            using var pen = new Pen(Color.White, UiTheme.Px(1.6f))
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
            };
            g.DrawLines(pen, new[]
            {
                new PointF(box.X + UiTheme.Px(4f), box.Y + UiTheme.Px(8.4f)),
                new PointF(box.X + UiTheme.Px(6.8f), box.Y + UiTheme.Px(11.2f)),
                new PointF(box.X + UiTheme.Px(12f), box.Y + UiTheme.Px(5.2f)),
            });
        }

        var color = Enabled ? UiTheme.TextPrimary : UiTheme.DisabledText;
        TextRenderer.DrawText(g, Text, Font,
            new Rectangle(UiTheme.Px(28), 0, Width - UiTheme.Px(28), Height), color,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }
}
