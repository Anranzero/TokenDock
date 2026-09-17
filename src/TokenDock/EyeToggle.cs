using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace TokenDock;

/// <summary>
/// 输入框内右侧的密码显示开关（自绘眼睛图标）：
/// 显示状态 = 睁眼（实心瞳孔），隐藏状态 = 闭眼（斜线）。悬停有反馈。
/// </summary>
internal sealed class EyeToggle : Control
{
    private bool _revealed;
    private bool _hover;

    public EyeToggle()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor
            | ControlStyles.UserPaint | ControlStyles.StandardClick,
            true);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
    }

    /// <summary>true = 明文（睁眼）。</summary>
    public bool Revealed
    {
        get => _revealed;
        set
        {
            if (_revealed == value) return;
            _revealed = value;
            Invalidate();
        }
    }

    public event Action? Toggled;

    protected override void OnMouseEnter(EventArgs e)
    {
        _hover = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Toggled?.Invoke();
        base.OnMouseDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var color = _hover ? UiTheme.TextPrimary : UiTheme.TextSecondary;
        using var pen = new Pen(color, UiTheme.Px(1.3f));
        var cx = Width / 2f;
        var cy = Height / 2f;
        var w = UiTheme.Px(16f);
        var h = UiTheme.Px(10f);

        g.DrawEllipse(pen, cx - w / 2f, cy - h / 2f, w, h);
        using (var fill = new SolidBrush(color))
        {
            var pupil = UiTheme.Px(_revealed ? 4.4f : 2.6f);
            g.FillEllipse(fill, cx - pupil / 2f, cy - pupil / 2f, pupil, pupil);
        }

        if (!_revealed)
            g.DrawLine(pen, cx - w / 2f, cy + h / 2f, cx + w / 2f, cy - h / 2f);
    }
}
