using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text.Json;
using System.Windows.Forms;

namespace TokenDock;

/// <summary>悬浮窗订阅的订阅源（下标与主窗页签一致，便于默认跟随当前页）。</summary>
public enum OverlaySubscription
{
    OpenCodeGo = 0,
    Codex = 1,
    Glm = 2
}

/// <summary>悬浮窗一行数据：标签 + 剩余百分比 + 倒计时（字段缺失时留空，不推算）。</summary>
public sealed record OverlayRow(string Label, double? Percent, string? Countdown);

/// <summary>悬浮窗展示模型（纯数据，由 Compose 组装，便于单测）。</summary>
public sealed record OverlayModel(
    string Title,
    bool HasData,
    IReadOnlyList<OverlayRow> Rows,
    string? Countdown,
    IReadOnlyList<string> Details,
    string? Hint);

/// <summary>悬浮窗展示模型组装：直接复用主程序的三个订阅状态，不发起任何请求。</summary>
public static class FloatingOverlayModel
{
    public static OverlayModel Compose(
        OverlaySubscription subscription, AppState? op, CodexState? codex, GlmState? glm)
    {
        return subscription switch
        {
            OverlaySubscription.Codex => ComposeCodex(codex),
            OverlaySubscription.Glm => ComposeGlm(glm),
            _ => ComposeOpenCode(op),
        };
    }

    private static OverlayModel ComposeOpenCode(AppState? state)
    {
        var title = "OpenCode Go";
        if (state?.LastGood is not { } data)
            return new OverlayModel(title, false, Array.Empty<OverlayRow>(), null,
                Array.Empty<string>(), state?.StatusText ?? "尚未获取数据");

        var rows = new List<OverlayRow>();
        var resets = new List<DateTimeOffset>();
        AddWindowRow(rows, resets, "5小时剩余", data.Rolling);
        AddWindowRow(rows, resets, "每周剩余", data.Weekly);

        var details = new List<string>();
        if (data.Monthly is not null)
            details.Add($"本月剩余 {Text(data.Monthly.RemainingPercent)} · {DisplayFormat.FormatCountdown(data.Monthly.ResetsAt, DateTimeOffset.UtcNow)}");
        details.Add("更新于 " + data.FetchedAtUtc.ToLocalTime().ToString("HH:mm:ss"));

        return new OverlayModel(title, true, rows,
            CountdownOf(resets), details, null);
    }

    private static OverlayModel ComposeCodex(CodexState? state)
    {
        const string title = "Codex";
        if (state is null || state.LastGood is null || state.Availability != CodexAvailability.Ready)
            return new OverlayModel(title, false, Array.Empty<OverlayRow>(), null,
                Array.Empty<string>(), state?.StatusText ?? "尚未获取 Codex 余量");

        var levels = CodexLevelView.Flatten(state.LastGood);
        if (levels.Count == 0)
            return new OverlayModel(title, false, Array.Empty<OverlayRow>(), null,
                Array.Empty<string>(), "接口未返回额度窗口");

        // 极简模式取两个窗口：第一个（通常是最短窗口）+ 7 天窗口（若存在且不同）
        var rows = new List<OverlayRow>();
        var resets = new List<DateTimeOffset>();
        var primary = levels[0];
        AddCodexRow(rows, resets, primary);
        var weekly = levels.FirstOrDefault(l =>
            l != primary && l.Window.WindowDurationMins is { } mins && mins >= 10080);
        if (weekly is not null) AddCodexRow(rows, resets, weekly);

        var details = new List<string>();
        if (state.Account?.LoggedIn == true)
            details.Add(state.Account.DisplayLine);
        foreach (var level in levels)
            details.Add($"{level.Title}：{Text(level.Window.RemainingPercent)} · {level.Window.CountdownText}");
        details.Add("更新于 " + state.LastGood.FetchedAtUtc.ToLocalTime().ToString("HH:mm:ss"));

        return new OverlayModel(title, true, rows, CountdownOf(resets), details, null);
    }

    private static OverlayModel ComposeGlm(GlmState? state)
    {
        const string title = "GLM";
        if (state?.LastGood is not { } data)
            return new OverlayModel(title, false, Array.Empty<OverlayRow>(), null,
                Array.Empty<string>(), state?.StatusText ?? "尚未获取 GLM 数据");

        var rows = new List<OverlayRow>();
        var resets = new List<DateTimeOffset>();
        var fiveHour = data.Limits.FirstOrDefault(l => GlmQuotaCard.GlmLimitLabel(l) == "5 小时剩余");
        var weekly = data.Limits.FirstOrDefault(l => GlmQuotaCard.GlmLimitLabel(l) == "每周剩余");
        AddGlmRow(rows, resets, "5小时剩余", fiveHour);
        AddGlmRow(rows, resets, "每周剩余", weekly);

        var details = new List<string>();
        foreach (var limit in data.Limits)
            details.Add($"{GlmQuotaCard.GlmLimitLabel(limit)}：剩余 {Text(RemainingOf(limit))}（上限 {N(limit.Usage)}）");
        if (data.TotalTokens is { } total) details.Add("总 Token：" + TokenUsageMath.FormatTokens((long)Math.Round(total)));
        if (data.TotalMcpCallCount is { } calls) details.Add("MCP 调用：" + ((long)Math.Round(calls)).ToString() + " 次");
        details.Add("更新于 " + data.FetchedAtUtc.ToLocalTime().ToString("HH:mm:ss"));

        return new OverlayModel(title, true, rows, CountdownOf(resets), details, data.PartialNote);
    }

    private static void AddWindowRow(List<OverlayRow> rows, List<DateTimeOffset> resets, string label, UsageWindow? window)
    {
        if (window is null) return;
        rows.Add(new OverlayRow(label, window.RemainingPercent,
            DisplayFormat.FormatCountdown(window.ResetsAt, DateTimeOffset.UtcNow)));
        if (window.ResetsAt is { } reset) resets.Add(reset);
    }

    private static void AddCodexRow(List<OverlayRow> rows, List<DateTimeOffset> resets, CodexLevelView level)
    {
        rows.Add(new OverlayRow(level.Title, level.Window.RemainingPercent, level.Window.CountdownText));
        if (level.Window.ResetsAtUnixSeconds is { } seconds)
            resets.Add(DateTimeOffset.FromUnixTimeSeconds(seconds));
    }

    private static void AddGlmRow(List<OverlayRow> rows, List<DateTimeOffset> resets, string label, GlmQuotaLimit? limit)
    {
        if (limit is null) return;
        rows.Add(new OverlayRow(label, RemainingOf(limit),
            limit.NextResetTime is { } reset ? DisplayFormat.FormatCountdown(reset, DateTimeOffset.UtcNow) : null));
        if (limit.NextResetTime is { } resetTime) resets.Add(resetTime);
    }

    /// <summary>GLM 额度的剩余百分比：剩余/上限 直算，缺失时用已用百分比反推（与额度卡同口径）。</summary>
    public static double? RemainingOf(GlmQuotaLimit limit)
        => limit.Usage is { } cap && cap > 0 && limit.Remaining is { } remaining
            ? Math.Clamp(remaining / cap * 100, 0, 100)
            : limit.Percentage is { } used ? Math.Clamp(100 - used, 0, 100) : null;

    private static string? CountdownOf(List<DateTimeOffset> resets)
        => resets.Count == 0 ? null : DisplayFormat.FormatCountdown(resets.Min(), DateTimeOffset.UtcNow);

    private static string Text(double? percent) => DisplayFormat.FormatRemainingPercent(percent);

    private static string N(double? value) => value?.ToString("0.##") ?? "—";
}

/// <summary>悬浮窗设置（非敏感，落盘 floating.json；重启恢复位置 / 订阅 / 透明度）。</summary>
public sealed class FloatingOverlaySettings
{
    public OverlaySubscription Subscription { get; set; } = OverlaySubscription.OpenCodeGo;
    public bool Enabled { get; set; }
    public bool Locked { get; set; }
    public double Opacity { get; set; } = 0.92;
    public int X { get; set; } = -1;
    public int Y { get; set; } = -1;

    public static double ClampOpacity(double value)
        => double.IsNaN(value) ? 0.92 : Math.Clamp(value, 0.6, 1.0);
}

public static class FloatingOverlayStore
{
    private static string FilePath => Path.Combine(AppDataPaths.Directory, "floating.json");

    public static FloatingOverlaySettings Load()
    {
        AppDataPaths.EnsureMigrated();
        return LoadFrom(FilePath);
    }

    public static void Save(FloatingOverlaySettings settings) => SaveTo(settings, FilePath);

    public static FloatingOverlaySettings LoadFrom(string path)
    {
        try
        {
            if (!File.Exists(path)) return new FloatingOverlaySettings();
            var loaded = JsonSerializer.Deserialize<FloatingOverlaySettings>(File.ReadAllText(path));
            if (loaded is null) return new FloatingOverlaySettings();
            loaded.Opacity = FloatingOverlaySettings.ClampOpacity(loaded.Opacity);
            return loaded;
        }
        catch (Exception)
        {
            return new FloatingOverlaySettings();
        }
    }

    public static void SaveTo(FloatingOverlaySettings settings, string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception)
        {
            // 落盘失败不影响本次运行
        }
    }
}

/// <summary>
/// 「悬浮余量」悬浮窗：始终置顶、不抢焦点（WS_EX_NOACTIVATE + TOOLWINDOW）、
/// 无边框自绘卡片。极简显示订阅名 + 两个额度行 + 最近重置倒计时；悬停展开详细信息。
/// 支持拖动、贴边吸附、锁定位置、透明度；数据复用主程序订阅状态（由托盘上下文推送）。
/// </summary>
internal sealed class FloatingOverlayForm : Form
{
    private readonly ContextMenuStrip _menu = new();
    private readonly ToolStripMenuItem _subscriptionItems = new("切换订阅");
    private readonly List<ToolStripMenuItem> _subscriptionChoices = new();
    private readonly ToolStripMenuItem _refreshItem = new("立即刷新");
    private readonly ToolStripMenuItem _lockItem = new("锁定位置");
    private readonly ToolStripMenuItem _opacityItem = new("透明度");
    private readonly List<ToolStripMenuItem> _opacityChoices = new();
    private readonly ToolStripMenuItem _hideItem = new("隐藏");
    private readonly ToolStripMenuItem _exitItem = new("退出悬浮");

    private OverlaySubscription _subscription = OverlaySubscription.OpenCodeGo;
    private OverlayModel _model = new("OpenCode Go", false, Array.Empty<OverlayRow>(), null, Array.Empty<string>(), "尚未获取数据");
    private bool _expanded;
    private bool _dragging;
    private Point _dragOffset;
    private AppState? _op;
    private CodexState? _codex;
    private GlmState? _glm;

    public event Action? RefreshRequested;
    public event Action? OpenMainRequested;
    public event Action? HideRequested;
    public event Action? ExitRequested;
    public event Action<OverlaySubscription>? SubscriptionChanged;
    public event Action<double>? OpacityChanged;
    public event Action<bool>? LockChanged;

    /// <summary>拖动结束（位置已确定）——托盘上下文据此保存位置。</summary>
    public event Action? MoveCommitted;

    public FloatingOverlayForm(FloatingOverlaySettings settings)
    {
        Subscription = settings.Subscription;
        Opacity = settings.Opacity;
        Locked = settings.Locked;

        Text = "TokenDock 悬浮余量";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        MinimizeBox = MaximizeBox = false;
        BackColor = UiTheme.Canvas;
        TopMost = true;

        BuildMenu();
        Appearance.Attach(this);
        UiTheme.Changed += OnThemeChanged;
        Disposed += (_, _) => UiTheme.Changed -= OnThemeChanged;
    }

    public OverlaySubscription Subscription
    {
        get => _subscription;
        set
        {
            if (_subscription == value) return;
            _subscription = value;
            RefreshModel();
            SyncMenuChecks();
            SubscriptionChanged?.Invoke(value);
        }
    }

    public bool Locked { get; private set; }

    /// <summary>托盘上下文推送三个订阅的当前状态（任何一路刷新后调用）。</summary>
    public void SetStates(AppState? op, CodexState? codex, GlmState? glm)
    {
        _op = op;
        _codex = codex;
        _glm = glm;
        RefreshModel();
    }

    /// <summary>测试探针：当前展示模型（订阅切换后应随之变化）。</summary>
    internal OverlayModel CurrentModel => _model;

    /// <summary>测试探针：订阅菜单项（模拟真实右键点击路径）。</summary>
    internal ToolStripMenuItem SubscriptionMenuItemForTest(int index) => _subscriptionChoices[index];

    /// <summary>按保存的位置显示（默认右下角，避开任务栏）；并应用主题窗口效果。</summary>
    public void ShowAt(FloatingOverlaySettings settings)
    {
        var area = Screen.PrimaryScreen!.WorkingArea;
        var size = CurrentSize();
        if (settings.X >= 0 && settings.Y >= 0)
            Location = new Point(settings.X, settings.Y);
        else
            Location = new Point(area.Right - size.Width - UiTheme.Px(24), area.Bottom - size.Height - UiTheme.Px(24));
        ClampInto(area);
        Show();
        ApplyWindowEffects();
    }

    /// <summary>把窗口收敛进指定工作区（多显示器以当前所在屏幕为准）。</summary>
    private void ClampInto(Rectangle area)
    {
        Location = new Point(
            Math.Clamp(Location.X, area.Left, Math.Max(area.Left, area.Right - Width)),
            Math.Clamp(Location.Y, area.Top, Math.Max(area.Top, area.Bottom - Height)));
    }

    /// <summary>主题 / 毛玻璃变化：重绘并重设窗口效果。</summary>
    private void OnThemeChanged()
    {
        if (IsDisposed) return;
        ApplyWindowEffects();
        Invalidate(true);
    }

    private void ApplyWindowEffects()
    {
        try
        {
            if (!IsHandleCreated) return;
            Opacity = Math.Clamp(Opacity, 0.6, 1.0);
            WindowEffects.Apply(Handle, UiTheme.IsGlass, (double)Opacity, UiTheme.IsDark);
        }
        catch (Exception)
        {
            // 效果失败保持半透明/不透明，不影响功能
        }
    }

    public void SetOpacity(double value)
    {
        Opacity = FloatingOverlaySettings.ClampOpacity(value);
        ApplyWindowEffects();
        OpacityChanged?.Invoke(Opacity);
    }

    public void SetLocked(bool locked)
    {
        if (Locked == locked) return;
        Locked = locked;
        _lockItem.Checked = locked;
        Invalidate();
        LockChanged?.Invoke(locked);
    }

    // ---- 尺寸与布局 ----

    private Size CurrentSize()
    {
        var width = UiTheme.Px(_expanded ? 252 : 204);
        int rows;
        if (_expanded && _model.HasData)
        {
            rows = _model.Rows.Count + _model.Details.Count;
            var height = UiTheme.Px(24 /*标题*/ + rows * 16 + (_model.Countdown is null ? 0 : 16) + 20);
            return new Size(width, height);
        }

        rows = Math.Min(_model.Rows.Count, 2);
        var compact = UiTheme.Px(24 + rows * 18 + (_model.Countdown is null ? 0 : 16) + 16);
        return new Size(width, compact);
    }

    private void Relayout()
    {
        var size = CurrentSize();
        var area = Screen.FromControl(this).WorkingArea;
        // 悬停展开时保持左上角不动，向右下扩展并收敛进工作区
        var x = Math.Min(Location.X, area.Right - size.Width);
        var y = Math.Min(Location.Y, area.Bottom - size.Height);
        Location = new Point(Math.Max(area.Left, x), Math.Max(area.Top, y));
        ClientSize = size;
        UpdateRoundedRegion();
    }

    /// <summary>圆角区域：窗口真正圆角（点击圆角外不命中）。</summary>
    private void UpdateRoundedRegion()
    {
        var radius = UiTheme.Px(12);
        using var path = UiTheme.RoundedRect(new RectangleF(0, 0, Width, Height), radius);
        Region?.Dispose();
        Region = new Region(path);
    }

    // ---- 悬停展开 / 拖动 / 点击 ----

    private void SetExpanded(bool value)
    {
        if (_expanded == value) return;
        _expanded = value;
        Relayout();
        Invalidate();
    }

    /// <summary>仅用于 --uipreview：模拟悬停展开 / 缩回。</summary>
    internal void PreviewSetExpanded(bool value) => SetExpanded(value);

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        SetExpanded(true);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        // 菜单打开或拖动中移出窗体不缩回，避免闪烁
        if (_menu.Visible || _dragging) return;
        SetExpanded(false);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || Locked) return;
        _dragging = true;
        _dragOffset = new Point(e.X, e.Y);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging) return;
        var area = Screen.FromControl(this).WorkingArea;
        var x = Left + e.X - _dragOffset.X;
        var y = Top + e.Y - _dragOffset.Y;
        // 贴边吸附：接近工作区边缘（含任务栏侧）时吸附，吸附距离 12px
        var snap = UiTheme.Px(12);
        x = Math.Abs(x - area.Left) <= snap ? area.Left
            : Math.Abs(x + Width - area.Right) <= snap ? area.Right - Width : x;
        y = Math.Abs(y - area.Top) <= snap ? area.Top
            : Math.Abs(y + Height - area.Bottom) <= snap ? area.Bottom - Height : y;
        Location = new Point(
            Math.Clamp(x, area.Left, Math.Max(area.Left, area.Right - Width)),
            Math.Clamp(y, area.Top, Math.Max(area.Top, area.Bottom - Height)));
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Right)
        {
            // 显式弹出菜单：无焦点窗口（WS_EX_NOACTIVATE）用 ContextMenuStrip 属性挂载时
            // 菜单有时收不到点击，手动 Show 更可靠
            _menu.Show(Cursor.Position);
            return;
        }

        if (_dragging) MoveCommitted?.Invoke();
        _dragging = false;
    }

    protected override void OnDoubleClick(EventArgs e)
    {
        base.OnDoubleClick(e);
        OpenMainRequested?.Invoke();
    }

    protected override void WndProc(ref Message m)
    {
        const int WmNcActivate = 0x0086;
        if (m.Msg == WmNcActivate) { m.Result = (IntPtr)1; return; } // 永不激活
        base.WndProc(ref m);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x00000008; // WS_EX_TOPMOST
            cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE：不抢输入焦点
            cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW：不进 Alt-Tab
            return cp;
        }
    }

    // ---- 菜单 ----

    private void BuildMenu()
    {
        _subscriptionChoices.Add(new ToolStripMenuItem("OpenCode Go"));
        _subscriptionChoices.Add(new ToolStripMenuItem("Codex"));
        _subscriptionChoices.Add(new ToolStripMenuItem("GLM"));
        for (var i = 0; i < _subscriptionChoices.Count; i++)
        {
            var index = i;
            _subscriptionChoices[i].Click += (_, _) => Subscription = (OverlaySubscription)index;
            _subscriptionItems.DropDownItems.Add(_subscriptionChoices[i]);
        }

        var opacities = new[] { ("100%", 1.0), ("88%", 0.88), ("75%", 0.75), ("60%", 0.6) };
        foreach (var (label, value) in opacities)
        {
            var item = new ToolStripMenuItem(label) { Tag = value };
            item.Click += (_, _) => SetOpacity(value);
            _opacityChoices.Add(item);
            _opacityItem.DropDownItems.Add(item);
        }

        _lockItem.CheckOnClick = true;
        _lockItem.CheckedChanged += (_, _) => SetLocked(_lockItem.Checked);
        _refreshItem.Click += (_, _) => RefreshRequested?.Invoke();
        _hideItem.Click += (_, _) => { HideRequested?.Invoke(); };
        _exitItem.Click += (_, _) => ExitRequested?.Invoke();

        _menu.Items.AddRange(new ToolStripItem[]
        {
            _subscriptionItems, _refreshItem, _lockItem, _opacityItem,
            new ToolStripSeparator(), _hideItem, _exitItem,
        });
        TrayMenuTheme.Apply(_menu);
        _menu.Closed += (_, _) =>
        {
            if (!Bounds.Contains(Cursor.Position)) SetExpanded(false);
        };
        SyncMenuChecks();
    }

    private void SyncMenuChecks()
    {
        for (var i = 0; i < _subscriptionChoices.Count; i++)
            _subscriptionChoices[i].Checked = i == (int)_subscription;
        _lockItem.Checked = Locked;
        foreach (var item in _opacityChoices)
            item.Checked = Math.Abs((double)item.Tag! - Opacity) < 0.01;
    }

    // ---- 绘制 ----

    private void RefreshModel()
    {
        _model = FloatingOverlayModel.Compose(_subscription, _op, _codex, _glm);
        Relayout();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = UiTheme.RoundedRect(new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f), UiTheme.Px(12f)))
        {
            using var fill = new SolidBrush(UiTheme.Card);
            g.FillPath(fill, path);
            using var pen = new Pen(UiTheme.IsGlass ? Color.FromArgb(90, 255, 255, 255) : UiTheme.CardBorder);
            g.DrawPath(pen, path);
        }

        var x = UiTheme.Px(12);
        var y = UiTheme.Px(8);

        // 标题行：订阅名 + 状态点（有数据且未过期=绿，否则灰/红）
        var dotColor = _model.HasData ? (IsStaleSelected() ? UiTheme.Gray : UiTheme.Green) : UiTheme.Gray;
        if (IsStaleSelected()) dotColor = UiTheme.Gray;
        using (var brush = new SolidBrush(dotColor))
            g.FillEllipse(brush, x, y + UiTheme.Px(5), UiTheme.Px(8), UiTheme.Px(8));
        TextRenderer.DrawText(g, _model.Title, UiTheme.Body,
            new Rectangle(x + UiTheme.Px(14), y, Width - x - UiTheme.Px(26), UiTheme.Px(18)),
            UiTheme.TextPrimary, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        y += UiTheme.Px(22);

        if (!_model.HasData)
        {
            DrawWrapped(g, _model.Hint ?? "尚未获取数据", x, y, Width - UiTheme.Px(24));
            return;
        }

        if (!_expanded)
        {
            // 极简：前两行（标签 + 剩余% + 迷你进度条）+ 最近重置倒计时
            for (var i = 0; i < Math.Min(_model.Rows.Count, 2); i++)
            {
                var row = _model.Rows[i];
                TextRenderer.DrawText(g, row.Label, UiTheme.Tiny, new Rectangle(x, y, UiTheme.Px(64), UiTheme.Px(14)),
                    UiTheme.TextSecondary, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                var percentText = row.Percent is { } p ? DisplayFormat.FormatRemainingPercent(p) : "未知";
                TextRenderer.DrawText(g, percentText, UiTheme.Tiny,
                    new Rectangle(Width - UiTheme.Px(64) - UiTheme.Px(10), y, UiTheme.Px(64), UiTheme.Px(14)),
                    UiTheme.TextPrimary, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                y += UiTheme.Px(14);

                if (row.Percent is { } percentValue)
                {
                    var barRect = new RectangleF(x, y + UiTheme.Px(1), Width - UiTheme.Px(24), UiTheme.Px(3));
                    using (var track = UiTheme.RoundedRect(barRect, UiTheme.Px(1.5f)))
                    using (var brush = new SolidBrush(UiTheme.Track))
                        g.FillPath(brush, track);
                    var fillWidth = (float)(barRect.Width * Math.Clamp(percentValue, 0, 100) / 100.0);
                    if (fillWidth > 0)
                    {
                        using var fillPath = UiTheme.RoundedRect(
                            new RectangleF(barRect.X, barRect.Y, Math.Max(fillWidth, (float)UiTheme.Px(3)), barRect.Height), UiTheme.Px(1.5f));
                        using var fillBrush = new SolidBrush(UiTheme.Green);
                        g.FillPath(fillBrush, fillPath);
                    }
                }

                y += UiTheme.Px(6);
            }

            if (_model.Countdown is not null)
            {
                TextRenderer.DrawText(g, _model.Countdown, UiTheme.Tiny,
                    new Rectangle(x, y, Width - UiTheme.Px(24), UiTheme.Px(14)),
                    UiTheme.TextSecondary, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            }

            return;
        }

        // 展开：全部行 + 详细信息
        foreach (var row in _model.Rows)
        {
            var line = row.Label + " " + (row.Percent is { } p ? DisplayFormat.FormatRemainingPercent(p) : "未知");
            if (row.Countdown is not null) line += " · " + row.Countdown;
            TextRenderer.DrawText(g, line, UiTheme.Tiny, new Rectangle(x, y, Width - UiTheme.Px(24), UiTheme.Px(14)),
                UiTheme.TextPrimary, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            y += UiTheme.Px(16);
        }

        foreach (var detail in _model.Details)
        {
            y = DrawWrapped(g, detail, x, y, Width - UiTheme.Px(24));
        }

        if (_model.Countdown is not null)
        {
            TextRenderer.DrawText(g, _model.Countdown, UiTheme.Tiny, new Rectangle(x, y, Width - UiTheme.Px(24), UiTheme.Px(14)),
                UiTheme.TextSecondary, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
    }

    /// <summary>画一行可能过长的文字（按词折行），返回新的 y。</summary>
    private int DrawWrapped(Graphics g, string text, int x, int y, int width)
    {
        var lines = new List<string>();
        var current = "";
        foreach (var ch in text)
        {
            if (TextRenderer.MeasureText(current + ch, UiTheme.Tiny).Width > width)
            {
                lines.Add(current);
                current = ch.ToString();
            }
            else
            {
                current += ch;
            }
        }

        if (current.Length > 0) lines.Add(current);
        foreach (var line in lines)
        {
            TextRenderer.DrawText(g, line, UiTheme.Tiny, new Rectangle(x, y, width, UiTheme.Px(14)),
                UiTheme.TextSecondary, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            y += UiTheme.Px(14);
        }

        return y;
    }

    private bool IsStaleSelected() => Subscription switch
    {
        OverlaySubscription.Codex => _codex?.IsStale == true,
        OverlaySubscription.Glm => _glm?.IsStale == true,
        _ => _op?.IsStale == true,
    };
}
