using System.Drawing;
using System.Windows.Forms;

namespace TokenDock;

/// <summary>
/// 「本机 Token 统计」卡：与官方套餐余量（/zen/go/v1/usage）彻底分离的独立区块。
/// 收起时仅显示一行来源摘要；展开后按客户端分块展示 数据来源 / 统计范围 / 按模型表格。
/// 只描述本机各客户端会话库留存的数据：不跨客户端合并为账号总量，
/// 记录缺失或客户端未安装也不显示为 0。
/// </summary>
internal sealed class LocalTokensCard : CardPanel
{
    public event Action? ExpandedChanged;

    private const int CollapsedHeight = 64;

    private readonly Label _name;
    private readonly Label _summary;
    private readonly LinkButton _toggle;
    private readonly Label _intro;
    private readonly Label _empty;
    private readonly Label _foot;
    private readonly List<ClientBlock> _blocks = new();
    private TokenUsageReport _report = TokenUsageReport.Unavailable("尚未采集");
    private bool _expanded;

    public LocalTokensCard()
    {
        _name = new Label
        {
            Text = "本机 Token 统计",
            AutoSize = true,
            BackColor = Color.Transparent,
            Font = UiTheme.Body,
            Location = new Point(UiTheme.Px(16), UiTheme.Px(12)),
        };
        UiTheme.Bind(_name, () => UiTheme.TextPrimary);
        _summary = new Label
        {
            AutoSize = false,
            BackColor = Color.Transparent,
            Size = new Size(UiTheme.Px(360), UiTheme.Px(16)),
            Font = UiTheme.Tiny,
            Location = new Point(UiTheme.Px(16), UiTheme.Px(38)),
        };
        UiTheme.Bind(_summary, () => UiTheme.TextSecondary);
        _toggle = new LinkButton
        {
            Text = "显示明细 ▾",
            Size = new Size(UiTheme.Px(96), UiTheme.Px(20)),
            Location = new Point(Width - UiTheme.Px(16) - UiTheme.Px(96), UiTheme.Px(12)),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
        };
        _toggle.Click += (_, _) => SetExpanded(!_expanded);

        // 说明行：明确“本机采集、不代表账号总量、缺失≠0”
        _intro = new Label
        {
            AutoSize = false,
            BackColor = Color.Transparent,
            Size = new Size(UiTheme.Px(360), UiTheme.Px(32)),
            Font = UiTheme.Tiny,
            Text = "本机会话库中 OpenCode Go 的请求记录，不代表账号总量。\n切换客户端后，无记录的客户端不代表账号用量为 0。",
            Visible = false,
        };
        UiTheme.Bind(_intro, () => UiTheme.TextSecondary);
        _empty = new Label
        {
            AutoSize = false,
            BackColor = Color.Transparent,
            Size = new Size(UiTheme.Px(360), UiTheme.Px(32)),
            Font = UiTheme.Tiny,
            Visible = false,
        };
        UiTheme.Bind(_empty, () => UiTheme.TextSecondary);
        _foot = new Label
        {
            AutoSize = false,
            BackColor = Color.Transparent,
            Size = new Size(UiTheme.Px(360), UiTheme.Px(32)),
            Font = UiTheme.Tiny,
            Text = "官方接口暂未提供账号级逐请求 / 逐模型 Token 历史。\n本页不做跨客户端合并，待官方开放 usage history 后接入。",
            Visible = false,
        };
        UiTheme.Bind(_foot, () => UiTheme.TextSecondary);

        Controls.AddRange(new Control[] { _name, _summary, _toggle, _intro, _empty, _foot });
        Height = UiTheme.Px(CollapsedHeight);
    }

    /// <summary>应用采集结果；收起时更新摘要行，展开时同步重建明细块。</summary>
    public void Update(TokenUsageReport report)
    {
        _report = report;
        _summary.Text = BuildSummary(report);
        if (_expanded)
            Rebuild();
    }

    public void SetExpanded(bool value)
    {
        if (_expanded == value) return;
        _expanded = value;
        _toggle.Text = _expanded ? "收起明细 ▴" : "显示明细 ▾";
        if (_expanded)
            Rebuild();
        else
        {
            foreach (var block in _blocks)
                block.Visible = false;
            _intro.Visible = _empty.Visible = _foot.Visible = false;
            Height = UiTheme.Px(CollapsedHeight);
        }
        ExpandedChanged?.Invoke();
    }

    private static string BuildSummary(TokenUsageReport report)
    {
        var time = report.ScannedAtUtc.ToLocalTime().ToString("HH:mm");
        if (!report.Available)
            return "未发现本机会话数据 · 采集于 " + time;
        var names = string.Join(" + ", report.Clients.Where(c => c.Available).Select(c => c.ClientName));
        return $"来源：本机 {names} 会话库 · 采集于 {time}";
    }

    /// <summary>展开态布局：说明行 → 各客户端块 → 尾注，按内容自适应高度。</summary>
    private void Rebuild()
    {
        _summary.Text = BuildSummary(_report);
        _intro.Visible = true;
        _foot.Visible = true;

        foreach (var block in _blocks)
            block.Visible = false;
        _blocks.Clear();

        var y = UiTheme.Px(64);
        _intro.Location = new Point(UiTheme.Px(16), y);
        y += _intro.Height + UiTheme.Px(8);

        if (_report.Available)
        {
            _empty.Visible = false;
            foreach (var client in _report.Clients)
            {
                var block = new ClientBlock { Width = Width - UiTheme.Px(32) };
                block.Update(client);
                block.Location = new Point(UiTheme.Px(16), y);
                _blocks.Add(block);
                Controls.Add(block);
                y += block.Height + UiTheme.Px(8);
            }

            y -= UiTheme.Px(8);
        }
        else
        {
            _empty.Visible = true;
            _empty.Text = $"未发现本机会话数据（{_report.Detail}）。\n这不代表账号用量为 0。";
            _empty.Location = new Point(UiTheme.Px(16), y);
            y += _empty.Height + UiTheme.Px(8);
        }

        _foot.Location = new Point(UiTheme.Px(16), y);
        y += _foot.Height + UiTheme.Px(12);

        Height = y;
    }

    /// <summary>单个客户端的统计块：客户端名 → 来源/范围 → 按模型表格（或原因说明）。</summary>
    private sealed class ClientBlock : CardPanel
    {
        private readonly Label _clientName;
        private readonly Label _meta;
        private readonly TokenTable _table;
        private readonly Label _note;

        public ClientBlock()
        {
            _clientName = new Label
            {
                AutoSize = true,
                BackColor = Color.Transparent,
                Font = UiTheme.Body,
                Location = new Point(UiTheme.Px(12), UiTheme.Px(10)),
            };
            UiTheme.Bind(_clientName, () => UiTheme.TextPrimary);
            _meta = new Label
            {
                AutoSize = false,
                BackColor = Color.Transparent,
                Size = new Size(UiTheme.Px(336), UiTheme.Px(32)),
                Font = UiTheme.Tiny,
            };
            UiTheme.Bind(_meta, () => UiTheme.TextSecondary);
            _table = new TokenTable
            {
                Size = new Size(UiTheme.Px(336), UiTheme.Px(36)),
            };
            _note = new Label
            {
                AutoSize = false,
                BackColor = Color.Transparent,
                Size = new Size(UiTheme.Px(336), UiTheme.Px(32)),
                Font = UiTheme.Tiny,
            };
            UiTheme.Bind(_note, () => UiTheme.TextSecondary);
            Controls.AddRange(new Control[] { _clientName, _meta, _table, _note });
        }

        public void Update(ClientTokenUsage client)
        {
            _clientName.Text = client.ClientName + " 客户端";

            var sourceLine = "来源：" + client.Source;
            if (client.FailedDatabaseCount > 0)
                sourceLine += $"（{client.FailedDatabaseCount} 个库读取失败）";
            string rangeLine;
            bool hasTable;

            if (!client.Available)
            {
                rangeLine = "本机未读到该客户端的会话库。";
                hasTable = false;
                _note.Text = $"未发现该客户端会话数据（{client.Detail}）——不代表账号用量为 0。";
            }
            else if (client.Records.Count == 0)
            {
                rangeLine = "统计范围：本机无记录。";
                hasTable = false;
                _note.Text = "该客户端本机库中没有 OpenCode Go 记录——不代表账号用量为 0。";
            }
            else
            {
                var earliest = client.EarliestUtc!.Value.ToLocalTime();
                var latest = client.LatestUtc!.Value.ToLocalTime();
                rangeLine = $"统计范围：{client.Records.Count} 条 · {earliest:yyyy-MM-dd} ~ {latest:yyyy-MM-dd}（本机时间）";
                hasTable = true;
                _note.Text = "";
                _table.SetRows(TokenUsageMath.Aggregate(client.Records));
            }

            _meta.Text = sourceLine + "\n" + rangeLine;
            LayoutBlock(hasTable);
        }

        private void LayoutBlock(bool hasTable)
        {
            var y = UiTheme.Px(32);
            _meta.Location = new Point(UiTheme.Px(12), y);
            y += _meta.Height + UiTheme.Px(8);

            if (hasTable)
            {
                _table.Visible = true;
                _note.Visible = false;
                _table.Location = new Point(UiTheme.Px(12), y);
                y += _table.Height + UiTheme.Px(4);
            }
            else
            {
                _table.Visible = false;
                _note.Visible = true;
                _note.Location = new Point(UiTheme.Px(12), y);
                y += _note.Height + UiTheme.Px(4);
            }

            Height = y + UiTheme.Px(8);
        }
    }
}

/// <summary>本机统计块内的 Token 表：模型 / 输入 / 输出 / 缓存 / 合计，超过 6 个模型折叠为“其他”。</summary>
internal sealed class TokenTable : Control
{
    private const int MaxRows = 6;

    private IReadOnlyList<ModelTokenTotals> _rows = Array.Empty<ModelTokenTotals>();
    private long _otherTotal;
    private int _otherCount;
    private bool _hasOther;

    public TokenTable()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor
            | ControlStyles.UserPaint,
            true);
        BackColor = Color.Transparent;
        Font = UiTheme.Tiny;
        UiTheme.Follow(this);
    }

    public void SetRows(IReadOnlyList<ModelTokenTotals> totals)
    {
        _hasOther = totals.Count > MaxRows;
        _otherCount = 0;
        _otherTotal = 0;
        var rows = new List<ModelTokenTotals>();
        if (_hasOther)
        {
            for (var i = 0; i < MaxRows; i++)
                rows.Add(totals[i]);
            for (var i = MaxRows; i < totals.Count; i++)
            {
                _otherCount++;
                _otherTotal += totals[i].Total;
            }
        }
        else
        {
            rows.AddRange(totals);
        }

        _rows = rows;
        Height = UiTheme.Px(18) * (1 + _rows.Count + (_hasOther ? 1 : 0));
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var rowHeight = UiTheme.Px(18);
        DrawRow(g, 0, "模型", "输入", "输出", "缓存", "合计", UiTheme.TextSecondary);
        for (var i = 0; i < _rows.Count; i++)
        {
            var totals = _rows[i];
            DrawRow(g, (i + 1) * rowHeight, totals.Model,
                TokenUsageMath.FormatTokens(totals.Input),
                TokenUsageMath.FormatTokens(totals.Output),
                TokenUsageMath.FormatTokens(totals.Cache),
                TokenUsageMath.FormatTokens(totals.Total),
                UiTheme.TextPrimary);
        }

        if (_hasOther)
        {
            DrawRow(g, (_rows.Count + 1) * rowHeight, $"其他 {_otherCount} 个模型", "", "", "",
                TokenUsageMath.FormatTokens(_otherTotal), UiTheme.TextSecondary);
        }
    }

    private void DrawRow(Graphics g, int y, string model, string input, string output, string cache, string total, Color color)
    {
        var numberWidth = UiTheme.Px(52);
        var modelWidth = Width - numberWidth * 4;
        TextRenderer.DrawText(g, model, Font, new Rectangle(0, y, modelWidth - UiTheme.Px(4), UiTheme.Px(18)), color,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);

        var x = modelWidth;
        foreach (var text in new[] { input, output, cache, total })
        {
            if (text.Length > 0)
            {
                TextRenderer.DrawText(g, text, Font, new Rectangle(x, y, numberWidth, UiTheme.Px(18)), color,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            }
            x += numberWidth;
        }
    }
}
