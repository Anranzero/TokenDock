using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace TokenDock;

/// <summary>顶部分段切换器（OpenCode Go / Codex），8px 栅格尺寸。</summary>
internal sealed class SegmentedControl : Control
{
    private readonly string[] _items;
    private int _selected;
    private int _hover = -1;

    public SegmentedControl(params string[] items)
    {
        _items = items;
        SetStyle(
            ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor
            | ControlStyles.UserPaint | ControlStyles.StandardClick,
            true);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        Font = UiTheme.Body;
        UiTheme.Follow(this);
    }

    public event Action<int>? SelectionChanged;

    public int SelectedIndex
    {
        get => _selected;
        set
        {
            if (value < 0 || value >= _items.Length || value == _selected) return;
            _selected = value;
            Invalidate();
            SelectionChanged?.Invoke(value);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var index = HitTest(e.X);
        if (index != _hover)
        {
            _hover = index;
            Invalidate();
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = -1;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        var index = HitTest(e.X);
        if (index >= 0 && index != _selected)
        {
            _selected = index;
            Invalidate();
            SelectionChanged?.Invoke(index);
        }
        base.OnMouseDown(e);
    }

    private int HitTest(int x)
    {
        if (_items.Length == 0) return -1;
        var width = (float)Width / _items.Length;
        var index = (int)(x / width);
        return index >= 0 && index < _items.Length ? index : -1;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var radius = UiTheme.Px(8f);
        using (var track = UiTheme.RoundedRect(new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f), radius))
        {
            using var trackBrush = new SolidBrush(UiTheme.SegmentTrack);
            g.FillPath(trackBrush, track);
        }

        var segmentWidth = (float)Width / _items.Length;
        for (var i = 0; i < _items.Length; i++)
        {
            var bounds = new RectangleF(i * segmentWidth + 2f, 2f, segmentWidth - 4f, Height - 4f);
            if (i == _selected)
            {
                using var selected = UiTheme.RoundedRect(bounds, radius - 2f);
                using var fill = new SolidBrush(UiTheme.Card);
                g.FillPath(fill, selected);
                using var pen = new Pen(UiTheme.CardBorder);
                g.DrawPath(pen, selected);
            }

            var color = i == _selected ? UiTheme.TextPrimary : i == _hover ? UiTheme.TextPrimary : UiTheme.TextSecondary;
            TextRenderer.DrawText(g, _items[i], Font, Rectangle.Round(bounds), color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
    }
}

/// <summary>单条 Codex 额度卡片：动态窗口名 + 剩余百分比 + 进度条 + 重置倒计时。</summary>
internal sealed class CodexLevelCard : CardPanel
{
    private readonly Label _title;
    private readonly PercentText _percent;
    private readonly FlatProgressBar _bar;
    private readonly Label _status;

    public CodexLevelCard()
    {
        _title = new Label
        {
            AutoSize = true,
            BackColor = Color.Transparent,
            Font = UiTheme.Body,
            Location = new Point(UiTheme.Px(16), UiTheme.Px(12)),
        };
        UiTheme.Bind(_title, () => UiTheme.TextPrimary);
        _percent = new PercentText
        {
            Size = new Size(UiTheme.Px(140), UiTheme.Px(38)),
            Location = new Point(Width - UiTheme.Px(156), UiTheme.Px(8)),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
        };
        _bar = new FlatProgressBar
        {
            Location = new Point(UiTheme.Px(16), UiTheme.Px(54)),
            Size = new Size(Width - UiTheme.Px(32), UiTheme.Px(6)),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };
        _status = new Label
        {
            AutoSize = true,
            BackColor = Color.Transparent,
            Font = UiTheme.Tiny,
            Location = new Point(UiTheme.Px(16), UiTheme.Px(70)),
        };
        UiTheme.Bind(_status, () => UiTheme.TextSecondary);
        Controls.AddRange(new Control[] { _title, _percent, _bar, _status });
        Height = UiTheme.Px(96);
    }

    public void Update(CodexLevelView level, bool stale)
    {
        _title.Text = level.Title;
        var window = level.Window;
        var color = stale ? UiTheme.Gray : UiTheme.Green;
        _bar.Set(window.RemainingPercent, color);
        _percent.Set(DisplayFormat.FormatRemainingNumber(window.RemainingPercent), "%", color);
        _status.Text = window.CountdownText;
    }
}

/// <summary>
/// Codex 页内容视图：账号行 → 额度卡片（分组×窗口展开）→ 金额未提供说明。
/// 只作为内容宿主，由主窗口负责它在窗体中的位置。
/// </summary>
internal sealed class CodexLevelsView : Panel
{
    private const string AmountNote = "模型、美元与人民币明细：未提供（无真实数据来源，不按百分比推算）。";

    private readonly Label _accountLine;
    private readonly Label _hintLine;
    private readonly Label _noteLine;
    private readonly List<CodexLevelCard> _cards = new();

    public CodexLevelsView()
    {
        BackColor = Color.Transparent;
        _accountLine = new Label
        {
            AutoSize = true,
            BackColor = Color.Transparent,
            Font = UiTheme.Tiny,
            Visible = false,
        };
        UiTheme.Bind(_accountLine, () => UiTheme.TextSecondary);
        _hintLine = new Label
        {
            AutoSize = false,
            BackColor = Color.Transparent,
            Font = UiTheme.Tiny,
            Size = new Size(UiTheme.Px(392), UiTheme.Px(40)),
            Visible = false,
        };
        UiTheme.Bind(_hintLine, () => UiTheme.TextSecondary);
        _noteLine = new Label
        {
            AutoSize = false,
            BackColor = Color.Transparent,
            Font = UiTheme.Tiny,
            Size = new Size(UiTheme.Px(392), UiTheme.Px(20)),
            Text = AmountNote,
            Visible = false,
        };
        UiTheme.Bind(_noteLine, () => UiTheme.TextSecondary);
        Controls.AddRange(new Control[] { _accountLine, _hintLine, _noteLine });
    }

    /// <summary>内容总高度（主窗口 Relayout 用）。</summary>
    public int ContentHeight { get; private set; }

    private static string Truncate(string text, int maxLength)
        => text.Length <= maxLength ? text : text[..maxLength] + "…";

    public void Update(CodexState state)
    {
        var x = UiTheme.Px(24);
        var y = 0;

        // 账号行
        _accountLine.Visible = state.Account is not null;
        if (state.Account is { } account)
        {
            _accountLine.Text = account.DisplayLine;
            _accountLine.Location = new Point(x, y + UiTheme.Px(4));
            y += UiTheme.Px(24);
        }

        // 提示行（未安装 / 未登录 / 登录中 / 失败原因）
        var hint = state.Availability switch
        {
            CodexAvailability.NotInstalled =>
                "未检测到 Codex CLI。先安装：npm i -g @openai/codex（需 Node.js 18+），安装后点「刷新」。",
            CodexAvailability.NotLoggedIn when state.LoginInProgress =>
                "已打开授权网页，请在浏览器完成登录；本页会自动刷新。",
            CodexAvailability.NotLoggedIn =>
                "尚未登录 ChatGPT。点击下方「登录 ChatGPT」完成授权（不要求 API Key，令牌由 Codex 本机保管）。",
            CodexAvailability.Failed => Truncate(state.StatusText, 110),
            _ => null,
        };
        _hintLine.Visible = hint is not null;
        if (hint is not null)
        {
            _hintLine.Text = hint;
            _hintLine.Location = new Point(x, y + UiTheme.Px(4));
            y += _hintLine.Height + UiTheme.Px(8);
        }

        // 额度卡片（分组 × 窗口展开；空字段不会产生空卡）
        var levels = state.LastGood is null
            ? Array.Empty<CodexLevelView>()
            : CodexLevelView.Flatten(state.LastGood);
        while (_cards.Count < levels.Count)
        {
            var card = new CodexLevelCard();
            _cards.Add(card);
            Controls.Add(card);
        }
        for (var i = 0; i < _cards.Count; i++)
        {
            var visible = i < levels.Count;
            _cards[i].Visible = visible;
            if (!visible) continue;
            _cards[i].Update(levels[i], state.IsStale);
            _cards[i].Width = UiTheme.Px(392);
            _cards[i].Location = new Point(x, y);
            y += _cards[i].Height + UiTheme.Px(16);
        }

        // 金额说明（始终展示，明确“未提供”，不推算）
        if (levels.Count > 0)
            y -= UiTheme.Px(16);
        _noteLine.Visible = true;
        _noteLine.Location = new Point(x, y + UiTheme.Px(12));
        y += UiTheme.Px(12) + _noteLine.Height;

        ContentHeight = y;
    }
}
