using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace TokenDock;

/// <summary>
/// GLM 页内容视图（与 Codex 页同构）：账号/套餐行 → 额度卡片（接口返回几条就几条，标签按
/// ZCode 确认的 (type, unit, number) 三元组映射）→ MCP 工具调用卡 → 模型 Token 表（列按接口
/// 实际提供动态决定）→ 统计范围与更新时间脚注。数据全部来自远端 Coding Plan 接口。
/// </summary>
internal sealed class GlmLevelsView : Panel
{
    private readonly Label _accountLine;
    private readonly Label _hintLine;
    private readonly Label _modelTitle;
    private readonly GlmModelTable _modelTable;
    private readonly Label _modelNote;
    private readonly Label _footLine1;
    private readonly Label _toolLine;
    private readonly Label _footLine2;
    private readonly List<GlmQuotaCard> _quotaCards = new();
    private readonly List<Control> _extraCards = new();

    public GlmLevelsView()
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
            Size = new Size(UiTheme.Px(392), UiTheme.Px(60)),
            Visible = false,
        };
        UiTheme.Bind(_hintLine, () => UiTheme.TextSecondary);

        _modelTitle = new Label
        {
            AutoSize = true,
            BackColor = Color.Transparent,
            Font = UiTheme.Body,
            Visible = false,
        };
        UiTheme.Bind(_modelTitle, () => UiTheme.TextPrimary);

        _modelTable = new GlmModelTable { Visible = false };
        _modelNote = new Label
        {
            AutoSize = false,
            BackColor = Color.Transparent,
            Font = UiTheme.Tiny,
            Size = new Size(UiTheme.Px(360), UiTheme.Px(32)),
            Visible = false,
        };
        UiTheme.Bind(_modelNote, () => UiTheme.TextSecondary);

        _footLine1 = new Label
        {
            AutoSize = false,
            BackColor = Color.Transparent,
            Font = UiTheme.Tiny,
            Size = new Size(UiTheme.Px(360), UiTheme.Px(16)),
            Visible = false,
        };
        UiTheme.Bind(_footLine1, () => UiTheme.TextSecondary);
        _toolLine = new Label
        {
            AutoSize = false,
            BackColor = Color.Transparent,
            Font = UiTheme.Tiny,
            Size = new Size(UiTheme.Px(360), UiTheme.Px(16)),
            Visible = false,
        };
        UiTheme.Bind(_toolLine, () => UiTheme.TextSecondary);
        _footLine2 = new Label
        {
            AutoSize = false,
            BackColor = Color.Transparent,
            Font = UiTheme.Tiny,
            Size = new Size(UiTheme.Px(360), UiTheme.Px(16)),
            Visible = false,
        };
        UiTheme.Bind(_footLine2, () => UiTheme.TextSecondary);

        Controls.AddRange(new Control[] { _accountLine, _hintLine, _modelTitle, _modelTable, _modelNote, _footLine1, _toolLine, _footLine2 });
    }

    /// <summary>内容总高度（主窗口 Relayout 用）。</summary>
    public int ContentHeight { get; private set; }

    /// <summary>渲染状态：成功数据 + 过期标记；失败且无数据时只显示原因。</summary>
    public void Update(GlmState state)
    {
        var x = UiTheme.Px(24);
        var y = 0;
        var data = state.LastGood;

        // 账号 / 套餐行
        _accountLine.Visible = data is not null;
        if (data is not null)
        {
            var plan = string.IsNullOrWhiteSpace(data.Level) ? "套餐：未返回" : FormatPlanLevel(data.Level!);
            _accountLine.Text = GlmEndpoints.DisplayName(data.Provider) + " · " + plan
                + (state.IsStale ? " · 数据已过期" : "");
            _accountLine.Location = new Point(x, y + UiTheme.Px(4));
            y += UiTheme.Px(24);
        }

        // 提示行（未设置密钥 / 失败原因）
        var hint = state.LastFailureKind switch
        {
            FetchFailureKind.NotConfigured => state.StatusText,
            FetchFailureKind.None => null,
            _ when data is null || state.IsStale => state.StatusText,
            _ => null,
        };
        _hintLine.Visible = hint is not null;
        if (hint is not null)
        {
            _hintLine.Text = Truncate(hint, 150);
            _hintLine.Location = new Point(x, y + UiTheme.Px(4));
            y += _hintLine.Height + UiTheme.Px(8);
        }

        // 额度卡片：接口返回几条画几条
        var limits = data?.Limits ?? Array.Empty<GlmQuotaLimit>();
        while (_quotaCards.Count < limits.Count)
        {
            var card = new GlmQuotaCard();
            _quotaCards.Add(card);
            Controls.Add(card);
        }

        for (var i = 0; i < _quotaCards.Count; i++)
        {
            var visible = i < limits.Count;
            _quotaCards[i].Visible = visible;
            if (!visible) continue;
            _quotaCards[i].Update(limits[i], state.IsStale);
            _quotaCards[i].Width = UiTheme.Px(392);
            _quotaCards[i].Location = new Point(x, y);
            y += _quotaCards[i].Height + UiTheme.Px(16);
        }

        // MCP 工具调用卡（额度存在时显示；调用次数来自 usage-detail?usageType=MCP）
        var mcpCard = EnsureExtraCard(0, () => new GlmMcpCard());
        var mcpVisible = data?.McpQuota is not null;
        mcpCard.Visible = mcpVisible;
        if (mcpVisible)
        {
            ((GlmMcpCard)mcpCard).Update(data!.McpQuota!, data.TotalMcpCallCount, state.IsStale);
            mcpCard.Width = UiTheme.Px(392);
            mcpCard.Location = new Point(x, y);
            y += mcpCard.Height + UiTheme.Px(16);
        }

        // 模型 Token 表
        var models = data?.Models ?? Array.Empty<GlmModelUsage>();
        _modelTitle.Visible = models.Count > 0;
        _modelTable.Visible = models.Count > 0;
        _modelNote.Visible = models.Count == 0 && data is not null;
        if (models.Count > 0)
        {
            var range = data!.RangeStart is { } start && data.RangeEnd is { } end
                ? $"{start.ToLocalTime():MM-dd} ~ {end.ToLocalTime():MM-dd}"
                : "本月";
            _modelTitle.Text = "模型 Token 消耗 · " + range;
            _modelTitle.Location = new Point(x, y);
            y += UiTheme.Px(24);

            _modelTable.Width = UiTheme.Px(392);
            _modelTable.SetRows(models);
            _modelTable.Location = new Point(x, y);
            y += _modelTable.Height + UiTheme.Px(8);
        }
        else if (_modelNote.Visible)
        {
            _modelNote.Text = data!.PartialNote?.Contains("模型用量明细") == true
                ? "模型 Token 明细：未获取（" + data.PartialNote + "）。"
                : "模型 Token 明细：接口未返回数据（可能该区间无调用）。";
            _modelNote.Location = new Point(x, y);
            y += _modelNote.Height + UiTheme.Px(8);
        }

        // 脚注：总计 / MCP 工具明细 / 统计范围与更新时间
        var foot1 = new List<string>();
        if (data?.TotalTokens is { } total) foot1.Add("总 Token：" + FormatTokens(total));
        if (data?.TotalModelCallCount is { } modelCalls) foot1.Add("模型调用：" + FormatCount(modelCalls) + " 次");
        if (data?.TotalMcpCallCount is { } mcpCalls) foot1.Add("MCP 调用：" + FormatCount(mcpCalls) + " 次");
        if (foot1.Count > 0)
        {
            _footLine1.Visible = true;
            _footLine1.Text = string.Join(" · ", foot1);
            _footLine1.Location = new Point(x, y);
            y += UiTheme.Px(18);
        }
        else
        {
            _footLine1.Visible = false;
        }

        // MCP 工具调用明细（接口返回 toolDataList/mcpDataList 时逐项列出）
        var toolParts = data?.McpTools
            .Where(t => t.CallCount is > 0)
            .Select(t => $"{t.Name} {FormatCount(t.CallCount!.Value)} 次")
            .ToList() ?? new List<string>();
        _toolLine.Visible = toolParts.Count > 0;
        if (toolParts.Count > 0)
        {
            _toolLine.Text = Truncate("MCP 工具：" + string.Join(" · ", toolParts), 150);
            _toolLine.Location = new Point(x, y);
            y += UiTheme.Px(18);
        }

        var foot2 = new List<string>();
        if (data is not null) foot2.Add("最后更新：" + data.FetchedAtUtc.ToLocalTime().ToString("HH:mm:ss"));
        if (!string.IsNullOrEmpty(data?.PartialNote)) foot2.Add(data!.PartialNote!);
        _footLine2.Visible = foot2.Count > 0;
        if (foot2.Count > 0)
        {
            _footLine2.Text = Truncate("数据来自 " + GlmEndpoints.DisplayName(data!.Provider) + " 远端账号统计 · " + string.Join(" · ", foot2), 150);
            _footLine2.Location = new Point(x, y);
            y += UiTheme.Px(18);
        }

        ContentHeight = Math.Max(y, UiTheme.Px(80));
    }

    private Control EnsureExtraCard(int index, Func<Control> factory)
    {
        while (_extraCards.Count <= index)
        {
            var card = factory();
            _extraCards.Add(card);
            Controls.Add(card);
        }

        return _extraCards[index];
    }

    private static string Truncate(string text, int maxLength)
        => text.Length <= maxLength ? text : text[..maxLength] + "…";

    /// <summary>套餐等级文案（individual-coding-plan → Individual Coding Plan）。</summary>
    private static string FormatPlanLevel(string level)
    {
        var cleaned = level.Trim().Replace('-', ' ').Replace('_', ' ');
        var parts = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < parts.Length; i++)
            parts[i] = parts[i].Length <= 1 ? parts[i].ToUpperInvariant() : char.ToUpperInvariant(parts[i][0]) + parts[i][1..];
        return string.Join(' ', parts);
    }

    /// <summary>紧凑 Token 数字（复用既有口径）。</summary>
    private static string FormatTokens(double value)
        => TokenUsageMath.FormatTokens((long)Math.Round(value));

    private static string FormatCount(double value)
        => value >= 1000 ? value.ToString("0.#,", CultureInfo.InvariantCulture) : ((long)value).ToString(CultureInfo.InvariantCulture);
}

/// <summary>单条额度卡：标签 + 剩余百分比 + 进度条 + 上限/剩余与重置倒计时（字段缺什么不显示什么）。</summary>
internal sealed class GlmQuotaCard : CardPanel
{
    private readonly Label _title;
    private readonly PercentText _percent;
    private readonly FlatProgressBar _bar;
    private readonly Label _status;

    public GlmQuotaCard()
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

    public void Update(GlmQuotaLimit limit, bool stale)
    {
        _title.Text = GlmLimitLabel(limit);

        // 剩余百分比优先用 剩余/上限 直算（上限是 usage 字段；number 是窗口大小如 5 小时/1 周）；
        // 无上限时用 percentage（接口给的是已用百分比）反推；两者都缺则不画
        var color = stale ? UiTheme.Gray : UiTheme.Green;
        double? remainingPercent = limit.Usage is { } cap && cap > 0 && limit.Remaining is { } remainingValue
            ? Math.Clamp(remainingValue / cap * 100, 0, 100)
            : limit.Percentage is { } used ? Math.Clamp(100 - used, 0, 100) : null;
        if (remainingPercent is { } rp)
        {
            _bar.Set(rp, color);
            _percent.Set(TokenDock.DisplayFormat.FormatRemainingNumber(rp), "%", color);
        }
        else
        {
            _bar.Set(0, UiTheme.Gray);
            _percent.SetText("接口未返回", UiTheme.Gray);
        }

        var parts = new List<string>();
        if (limit.Usage is { } limitCap) parts.Add("上限 " + Trim(limitCap));
        if (limit.Remaining is { } remaining) parts.Add("剩余 " + Trim(remaining));
        else if (limit.CurrentValue is { } current) parts.Add("已用 " + Trim(current));
        if (limit.NextResetTime is { } reset) parts.Add(TokenDock.DisplayFormat.FormatCountdown(reset, DateTimeOffset.UtcNow));
        _status.Text = parts.Count > 0 ? string.Join(" · ", parts) : DisplayFormat.FormatStatus(null);
    }

    /// <summary>
    /// 额度标签：按 ZCode 客户端的取数三元组映射——CREDIT_LIMIT 与 TOKENS_LIMIT 是等价组
    /// （ZCode `GYe` 判等逻辑），unit=3+number=5 → 5 小时，unit=6 → 每周；TIME_LIMIT/unit5/number1 → 工具调用。
    /// 未命中时显示原始 type，不臆测。
    /// </summary>
    internal static string GlmLimitLabel(GlmQuotaLimit limit)
    {
        var type = limit.Type.Trim().ToUpperInvariant();
        var isTokenGroup = type is "CREDIT_LIMIT" or "TOKENS_LIMIT";
        var isTimeGroup = type == "TIME_LIMIT";
        var unit = limit.Unit;
        var number = limit.Number;
        if (isTokenGroup && unit == 3 && number == 5) return "5 小时剩余";
        if (isTokenGroup && unit == 6) return "每周剩余";
        if (isTimeGroup && unit == 5 && number == 1) return "工具调用（MCP）";
        if (isTokenGroup) return "Token 额度（" + Trim(unit) + "）";
        if (isTimeGroup) return "时长额度（" + Trim(unit) + "）";
        return limit.Type;
    }

    private static string Trim(double? value) => value is null ? "—" : Trim(value.Value);

    private static string Trim(double value)
        => Math.Abs(value - Math.Round(value)) < 0.0001
            ? ((long)Math.Round(value)).ToString(CultureInfo.InvariantCulture)
            : value.ToString("0.##", CultureInfo.InvariantCulture);
}

/// <summary>MCP 月度额度卡：used / limit / remaining + 调用次数 + 下次刷新。</summary>
internal sealed class GlmMcpCard : CardPanel
{
    private readonly Label _title;
    private readonly PercentText _percent;
    private readonly FlatProgressBar _bar;
    private readonly Label _status;

    public GlmMcpCard()
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

    public void Update(GlmMcpQuota quota, double? callCount, bool stale)
    {
        _title.Text = "MCP 月度额度";

        double? remainingPercent = quota is { Remaining: { } remaining, Limit: { } totalLimit } && totalLimit > 0
            ? Math.Clamp(remaining / totalLimit * 100, 0, 100)
            : null;
        var color = stale ? UiTheme.Gray : UiTheme.Green;
        if (remainingPercent is { } rp)
        {
            _bar.Set(rp, color);
            _percent.Set(DisplayFormat.FormatRemainingNumber(rp), "%", color);
        }
        else
        {
            _bar.Set(0, UiTheme.Gray);
            _percent.SetText("接口未返回", UiTheme.Gray);
        }

        var parts = new List<string>();
        if (quota.Used is { } used && quota.Limit is { } limit)
            parts.Add($"已用 {Trim(used)} / {Trim(limit)}");
        if (callCount is { } calls) parts.Add("调用 " + FormatCount(calls) + " 次");
        if (!string.IsNullOrWhiteSpace(quota.Level)) parts.Add("等级 " + quota.Level!.Trim());
        if (quota.NextRefreshAt is { } next) parts.Add(DisplayFormat.FormatCountdown(next, DateTimeOffset.UtcNow));
        _status.Text = parts.Count > 0 ? string.Join(" · ", parts) : "接口未提供明细";
    }

    private static string Trim(double value)
        => Math.Abs(value - Math.Round(value)) < 0.0001
            ? ((long)Math.Round(value)).ToString(CultureInfo.InvariantCulture)
            : value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string FormatCount(double value)
        => ((long)Math.Round(value)).ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// 模型 Token 表（动态列）：模型 / 缓存输入 / 输入 / 输出 / 合计；接口未提供的列整列不显示，
/// 单行缺值时留空——绝不推算。
/// </summary>
internal sealed class GlmModelTable : Control
{
    private const int MaxRows = 8;

    private IReadOnlyList<GlmModelUsage> _rows = Array.Empty<GlmModelUsage>();
    private bool _hasCached;
    private bool _hasUncached;
    private bool _hasOutput;
    private bool _hasTotal;
    private bool _hasCredits;
    private int _otherCount;
    private double _otherTotal;

    public GlmModelTable()
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

    public void SetRows(IReadOnlyList<GlmModelUsage> models)
    {
        _hasCached = models.Any(m => m.CachedInputTokens is not null);
        _hasUncached = models.Any(m => m.UncachedInputTokens is not null);
        _hasOutput = models.Any(m => m.OutputTokens is not null);
        _hasTotal = models.Any(m => m.TotalTokens is not null);
        _hasCredits = models.Any(m => m.TotalCredits is not null);

        var ordered = models.OrderByDescending(m => m.TotalTokens ?? 0).ToList();
        _otherCount = 0;
        _otherTotal = 0;
        if (ordered.Count > MaxRows)
        {
            for (var i = MaxRows; i < ordered.Count; i++)
            {
                _otherCount++;
                _otherTotal += ordered[i].TotalTokens ?? 0;
            }
            ordered = ordered.Take(MaxRows).ToList();
        }

        _rows = ordered;
        Height = UiTheme.Px(18) * (1 + _rows.Count + (_otherCount > 0 ? 1 : 0));
        Invalidate();
    }

    private int ColumnCount
        => 1 + (_hasCached ? 1 : 0) + (_hasUncached ? 1 : 0) + (_hasOutput ? 1 : 0) + (_hasTotal ? 1 : 0) + (_hasCredits ? 1 : 0);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var rowHeight = UiTheme.Px(18);
        var headers = new List<string> { "模型" };
        if (_hasCached) headers.Add("缓存输入");
        if (_hasUncached) headers.Add("输入");
        if (_hasOutput) headers.Add("输出");
        if (_hasTotal) headers.Add("合计");
        if (_hasCredits) headers.Add("积分");
        DrawRow(g, 0, headers, UiTheme.TextSecondary);

        for (var i = 0; i < _rows.Count; i++)
        {
            var m = _rows[i];
            var cells = new List<string> { m.ModelName };
            if (_hasCached) cells.Add(Format(m.CachedInputTokens));
            if (_hasUncached) cells.Add(Format(m.UncachedInputTokens));
            if (_hasOutput) cells.Add(Format(m.OutputTokens));
            if (_hasTotal) cells.Add(Format(m.TotalTokens));
            if (_hasCredits) cells.Add(Format(m.TotalCredits));
            DrawRow(g, (i + 1) * rowHeight, cells, UiTheme.TextPrimary);
        }

        if (_otherCount > 0)
        {
            var cells = new List<string> { $"其他 {_otherCount} 个模型" };
            while (cells.Count < headers.Count - 1) cells.Add("");
            if (_hasCredits && !_hasTotal) cells.Add("");
            else if (_hasTotal) cells.Add(TokenUsageMath.FormatTokens((long)Math.Round(_otherTotal)));
            DrawRow(g, (_rows.Count + 1) * rowHeight, cells, UiTheme.TextSecondary);
        }
    }

    private void DrawRow(Graphics g, int y, IReadOnlyList<string> cells, Color color)
    {
        var numberWidth = UiTheme.Px(60);
        var numbers = cells.Count - 1;
        var modelWidth = Math.Max(UiTheme.Px(80), Width - numberWidth * numbers);
        TextRenderer.DrawText(g, cells[0], Font, new Rectangle(0, y, modelWidth - UiTheme.Px(4), UiTheme.Px(18)), color,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);

        var x = modelWidth;
        for (var i = 1; i < cells.Count; i++)
        {
            if (cells[i].Length > 0)
            {
                TextRenderer.DrawText(g, cells[i], Font, new Rectangle(x, y, numberWidth, UiTheme.Px(18)), color,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            }
            x += numberWidth;
        }
    }

    /// <summary>Token 数紧凑格式；积分保留一位小数（单位未知，按接口原值展示）。</summary>
    private static string Format(double? value)
    {
        if (value is null) return "";
        var v = value.Value;
        return v >= 1000
            ? TokenUsageMath.FormatTokens((long)Math.Round(v))
            : Math.Abs(v - Math.Round(v)) < 0.0001
                ? ((long)Math.Round(v)).ToString(CultureInfo.InvariantCulture)
                : v.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
