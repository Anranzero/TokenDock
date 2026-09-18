using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace TokenDock;

/// <summary>
/// 主窗口（Win11 原生标题栏，固定宽度 440、禁用最大化）：
/// 顶部：分段切换（OpenCode Go / Codex）+ 状态圆点/更新时间 + 设置入口；
/// 中部：两页内容——OpenCode Go 页为三张官方余量卡（仅 /zen/go/v1/usage 数据）+
/// 独立的「本机 Token 统计」卡（本机会话库采集，与官方数据分离）；Codex 页为额度卡片。
/// 底部：刷新 / 页面动作 / 最小化到托盘。X 关闭即隐藏到托盘；错误一律内联提示。
/// </summary>
internal sealed class DetailForm : Form
{
    private readonly AppState _state;
    private readonly Action _refreshNow;
    private readonly Action _openSettings;
    private readonly SegmentedControl _pages;
    private readonly StatusChip _status;
    private readonly Spinner _spinner;
    private readonly Label _detailStatus;
    private readonly UsageCard _rolling;
    private readonly UsageCard _weekly;
    private readonly UsageCard _monthly;
    private readonly LocalTokensCard _local;
    private readonly CodexLevelsView _codexView;
    private readonly GlmLevelsView _glmView;
    private readonly IconButton _btnOverlay;
    private readonly PillButton _btnRefresh;
    private readonly PillButton _btnKeys;
    private readonly PillButton _btnHide;
    private readonly ToolTip _tips = new();
    private CodexState _codexState = new();
    private GlmState _glmState = new();
    private int _page;
    private int _busy;
    private bool _hideOnClose = true;

    /// <summary>Codex 页请求登录（由托盘上下文执行登录流程）。</summary>
    public event Action? CodexLoginRequested;

    /// <summary>Codex 页请求刷新。</summary>
    public event Action? CodexRefreshRequested;

    /// <summary>GLM 页请求刷新 / 打开 GLM 设置（由托盘上下文执行）。</summary>
    public event Action? GlmRefreshRequested;

    public event Action? GlmSettingsRequested;

    /// <summary>页面切换（0=OpenCode Go，1=Codex，2=GLM），由托盘上下文决定是否刷新。</summary>
    public event Action<int>? PageChanged;

    /// <summary>悬浮余量开关（顶栏图钉按钮），由托盘上下文创建 / 隐藏悬浮窗。</summary>
    public event Action? OverlayToggleRequested;

    public DetailForm(AppState state, Action refreshNow, Action openSettings)
    {
        _state = state;
        _refreshNow = refreshNow;
        _openSettings = openSettings;

        Text = "TokenDock · AI 用量助手";
        try { Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!); } catch (ArgumentException) { }
        Font = UiTheme.Body;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = true;
        ShowInTaskbar = true;
        StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(UiTheme.Px(440), UiTheme.Px(436));
        Appearance.Attach(this); // 背景色随主题 + 毛玻璃/半透明窗口效果（即时切换）

        // 顶部：分段切换 + 状态 + 悬浮开关 + 设置入口
        _pages = new SegmentedControl("OpenCode Go", "Codex", "GLM")
        {
            Size = new Size(UiTheme.Px(248), UiTheme.Px(28)),
            Location = new Point(UiTheme.Px(24), UiTheme.Px(20)),
        };
        _pages.SelectionChanged += index => SetPage(index);
        _status = new StatusChip();
        _spinner = new Spinner
        {
            Size = new Size(UiTheme.Px(16), UiTheme.Px(16)),
            Location = new Point(UiTheme.Px(280), UiTheme.Px(26)),
        };
        var btnOverlay = new IconButton("\uE718")
        {
            Size = new Size(UiTheme.Px(32), UiTheme.Px(32)),
            Location = new Point(ClientSize.Width - UiTheme.Px(24) - UiTheme.Px(32) - UiTheme.Px(36), UiTheme.Px(16)),
        };
        _btnOverlay = btnOverlay;
        btnOverlay.Click += (_, _) => OverlayToggleRequested?.Invoke();
        _tips.SetToolTip(btnOverlay, "悬浮余量");
        var btnGear = new IconButton("\uE713")
        {
            Size = new Size(UiTheme.Px(32), UiTheme.Px(32)),
            Location = new Point(ClientSize.Width - UiTheme.Px(24) - UiTheme.Px(32), UiTheme.Px(16)),
        };
        btnGear.Click += (_, _) => _openSettings();
        _tips.SetToolTip(btnGear, "设置");

        // OpenCode Go 页内容：三张官方余量卡（仅接口数据）+ 独立的本机 Token 统计卡
        _rolling = new UsageCard("5小时用量");
        _weekly = new UsageCard("本周");
        _monthly = new UsageCard("本月");
        _local = new LocalTokensCard();
        _local.ExpandedChanged += Relayout;

        _detailStatus = new Label
        {
            AutoSize = false,
            BackColor = Color.Transparent,
            Size = new Size(UiTheme.Px(392), UiTheme.Px(40)),
            Font = UiTheme.Tiny,
            Visible = false,
        };
        UiTheme.Bind(_detailStatus, () => UiTheme.TextSecondary);

        // Codex 页内容
        _codexView = new CodexLevelsView { Location = new Point(0, 0), Visible = false };

        // GLM 页内容（远端 Coding Plan 账号统计）
        _glmView = new GlmLevelsView { Location = new Point(0, 0), Visible = false };

        // 底部：三按钮（刷新 / 页面动作 / 最小化到托盘），等宽居中
        _btnRefresh = new PillButton(primary: true) { Text = "刷新", Size = new Size(UiTheme.Px(112), UiTheme.Px(32)) };
        _btnKeys = new PillButton() { Text = "设置密钥", Size = new Size(UiTheme.Px(112), UiTheme.Px(32)) };
        _btnHide = new PillButton() { Text = "最小化到托盘", Size = new Size(UiTheme.Px(112), UiTheme.Px(32)) };
        _btnRefresh.Click += (_, _) =>
        {
            if (_page == 0) _refreshNow();
            else if (_page == 1) CodexRefreshRequested?.Invoke();
            else GlmRefreshRequested?.Invoke();
        };
        _btnKeys.Click += (_, _) =>
        {
            if (_page == 0) _openSettings();
            else if (_page == 1) CodexLoginRequested?.Invoke();
            else GlmSettingsRequested?.Invoke();
        };
        _btnHide.Click += (_, _) => Hide();

        Controls.AddRange(new Control[]
        {
            _pages, _status, _spinner, btnOverlay, btnGear,
            _rolling, _weekly, _monthly, _local, _detailStatus, _codexView, _glmView,
            _btnRefresh, _btnKeys, _btnHide,
        });
        Relayout();
        UpdateState(_state);
        UpdateCodexState(_codexState);
    }

    /// <summary>固定宽度工具窗口：剥离最大化样式，标题栏只保留最小化与关闭。</summary>
    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.Style &= ~0x00010000; // WS_MAXIMIZEBOX
            return cp;
        }
    }

    /// <summary>拦截最大化命令（含 Win+Up / 双击标题栏），避免固定布局被拉伸。</summary>
    protected override void WndProc(ref Message m)
    {
        const int WmSysCommand = 0x0112;
        const int ScMaximize = 0xF030;
        if (m.Msg == WmSysCommand && (m.WParam.ToInt32() & 0xFFF0) == ScMaximize)
            return;
        base.WndProc(ref m);
    }

    // ---------- 页面切换 ----------

    private void SetPage(int page)
    {
        _page = page;
        var openCode = page == 0;

        foreach (var card in new Control[] { _rolling, _weekly, _monthly, _local })
            card.Visible = openCode;
        _codexView.Visible = page == 1;
        _glmView.Visible = page == 2;
        _detailStatus.Visible = openCode && !string.IsNullOrEmpty(_detailStatus.Text);

        if (page == 1)
            _codexView.Update(_codexState);
        if (page == 2)
            _glmView.Update(_glmState);

        UpdateMiddleButton();
        ApplyChip();
        Relayout();
        PageChanged?.Invoke(page);
    }

    private void UpdateMiddleButton()
    {
        if (_page == 0)
        {
            _btnKeys.Text = "设置密钥";
            _btnKeys.Enabled = true;
            return;
        }

        if (_page == 2)
        {
            _btnKeys.Text = "GLM 密钥";
            _btnKeys.Enabled = true;
            return;
        }

        var state = _codexState;
        if (state.Availability == CodexAvailability.Ready || state.Account?.LoggedIn == true)
        {
            _btnKeys.Text = "已登录";
            _btnKeys.Enabled = false;
        }
        else if (state.LoginInProgress)
        {
            _btnKeys.Text = "等待登录…";
            _btnKeys.Enabled = false;
        }
        else if (state.Availability == CodexAvailability.NotInstalled)
        {
            _btnKeys.Text = "未检测到 Codex";
            _btnKeys.Enabled = false;
        }
        else
        {
            _btnKeys.Text = "登录 ChatGPT";
            _btnKeys.Enabled = true;
        }
    }

    // ---------- 状态 ----------

    /// <summary>刷新中：状态行小转圈 + 刷新按钮禁用；并发刷新用计数器收敛。</summary>
    public void SetRefreshing(bool busy)
    {
        _busy += busy ? 1 : -1;
        if (_busy < 0) _busy = 0;
        _spinner.Visible = _busy > 0;
        _btnRefresh.Enabled = _busy == 0;
        SetChip(_busy > 0 ? "刷新中…" : string.Empty, UiTheme.Gray);
        if (_busy == 0) ApplyChip();
    }

    /// <summary>状态胶囊固定在顶栏第二行左侧（全宽可用，不再与右侧图标抢空间）。</summary>
    private void SetChip(string text, Color dot)
    {
        _status.Set(text, dot);
        _status.Location = new Point(UiTheme.Px(24), UiTheme.Px(52));
    }

    /// <summary>悬浮开关按钮状态（图钉图标：未开=描边灰，开启=绿色实心钉）。</summary>
    public void SetOverlayActive(bool active)
    {
        _btnOverlay.Text = active ? "\uE719" : "\uE718";
        _btnOverlay.ForeColor = active ? UiTheme.Green : UiTheme.TextSecondary;
        _tips.SetToolTip(_btnOverlay, active ? "悬浮余量：已开启（点击关闭）" : "悬浮余量");
    }

    private void ApplyChip()
    {
        if (_page == 0)
        {
            ApplyOpenCodeChip(_state);
            return;
        }

        if (_page == 2)
        {
            ApplyGlmChip();
            return;
        }

        var state = _codexState;
        if (state.LastGood is not null && !state.IsStale && state.Availability == CodexAvailability.Ready)
        {
            SetChip("正常 · 更新于 " + state.LastGood.FetchedAtUtc.ToLocalTime().ToString("HH:mm"), UiTheme.Green);
        }
        else if (state.IsStale)
        {
            var time = state.LastGood is { } good ? " · 更新于 " + good.FetchedAtUtc.ToLocalTime().ToString("HH:mm") : "";
            SetChip("已过期" + time, UiTheme.Gray);
        }
        else if (state.Availability == CodexAvailability.NotInstalled)
        {
            SetChip("未检测到 Codex", UiTheme.Gray);
        }
        else if (state.Availability == CodexAvailability.NotLoggedIn)
        {
            SetChip("未登录 ChatGPT", UiTheme.Gray);
        }
        else if (state.Availability == CodexAvailability.Failed)
        {
            SetChip("获取失败", UiTheme.Red);
        }
        else
        {
            SetChip("尚未获取", UiTheme.Gray);
        }
    }

    public void UpdateState(AppState state)
    {
        var data = state.LastGood;
        _rolling.Update(data?.Rolling, state.IsStale);
        _weekly.Update(data?.Weekly, state.IsStale);
        _monthly.Update(data?.Monthly, state.IsStale);

        if (data is not null && !state.IsStale)
        {
            _detailStatus.Text = string.Empty;
        }
        else if (state.IsStale)
        {
            _detailStatus.Text = state.StatusText;
        }
        else if (state.LastFailureKind == FetchFailureKind.NotConfigured)
        {
            _detailStatus.Text = state.StatusText;
        }
        else if (state.LastFailureKind != FetchFailureKind.None)
        {
            _detailStatus.Text = state.StatusText;
        }
        else
        {
            _detailStatus.Text = string.Empty;
        }

        if (_page == 0) ApplyOpenCodeChip(state);
        _detailStatus.Visible = _page == 0 && !string.IsNullOrEmpty(_detailStatus.Text);
        Relayout();
    }

    private void ApplyOpenCodeChip(AppState state)
    {
        var data = state.LastGood;
        if (data is not null && !state.IsStale)
        {
            if (NextReset(data) is { } soon && (soon.resetsAt - DateTimeOffset.UtcNow).TotalMinutes <= 30)
                SetChip("即将重置 · 更新于 " + data.FetchedAtUtc.ToLocalTime().ToString("HH:mm"), UiTheme.Green);
            else
                SetChip("正常 · 更新于 " + data.FetchedAtUtc.ToLocalTime().ToString("HH:mm"), UiTheme.Green);
        }
        else if (state.IsStale)
        {
            SetChip("已过期 · 更新于 " + data!.FetchedAtUtc.ToLocalTime().ToString("HH:mm"), UiTheme.Gray);
        }
        else if (state.LastFailureKind == FetchFailureKind.NotConfigured)
        {
            SetChip("未设置密钥", UiTheme.Gray);
        }
        else if (state.LastFailureKind != FetchFailureKind.None)
        {
            SetChip("获取失败", UiTheme.Red);
        }
        else
        {
            SetChip("尚未获取", UiTheme.Gray);
        }
    }

    public void UpdateCodexState(CodexState state)
    {
        _codexState = state;
        _codexView.Update(state);
        if (_page == 1)
        {
            ApplyChip();
            UpdateMiddleButton();
            Relayout();
        }
        else
        {
            UpdateMiddleButton();
        }
    }

    /// <summary>应用 GLM 采集状态（远端 Coding Plan 账号统计）。</summary>
    public void UpdateGlmState(GlmState state)
    {
        _glmState = state;
        _glmView.Update(state);
        if (_page == 2)
        {
            ApplyChip();
            Relayout();
        }
    }

    /// <summary>GLM 页状态胶囊：正常 / 已过期 / 获取失败 / 未设置密钥。</summary>
    private void ApplyGlmChip()
    {
        var state = _glmState;
        if (state.LastGood is not null && !state.IsStale)
        {
            SetChip("正常 · 更新于 " + state.LastGood.FetchedAtUtc.ToLocalTime().ToString("HH:mm"), UiTheme.Green);
        }
        else if (state.IsStale)
        {
            var time = state.LastGood is { } good ? " · 更新于 " + good.FetchedAtUtc.ToLocalTime().ToString("HH:mm") : "";
            SetChip("已过期" + time, UiTheme.Gray);
        }
        else if (state.LastFailureKind == FetchFailureKind.NotConfigured)
        {
            SetChip("未设置 GLM 密钥", UiTheme.Gray);
        }
        else if (state.LastFailureKind != FetchFailureKind.None)
        {
            SetChip("获取失败", UiTheme.Red);
        }
        else
        {
            SetChip("尚未获取", UiTheme.Gray);
        }
    }

    /// <summary>应用本机 Token 采集结果（独立的「本机 Token 统计」卡，与官方余量卡无关）。</summary>
    public void UpdateLocalTokens(TokenUsageReport report)
    {
        _local.Update(report);
        if (_page == 0) Relayout();
    }

    private static (string label, DateTimeOffset resetsAt)? NextReset(UsageData data)
    {
        DateTimeOffset? best = null;
        var label = string.Empty;
        void Consider(string name, UsageWindow? window)
        {
            if (window?.ResetsAt is { } r && (best is null || r < best))
            {
                best = r;
                label = name;
            }
        }

        Consider("5小时用量", data.Rolling);
        Consider("本周", data.Weekly);
        Consider("本月", data.Monthly);
        return best is null ? null : (label, best.Value);
    }

    /// <summary>8px 栅格重排：按当前页内容计算高度，底部三按钮等宽居中。</summary>
    private void Relayout()
    {
        int contentBottom;
        if (_page == 0)
        {
            // 状态胶囊占顶栏第二行（52..76），卡片从 80 开始
            var x = UiTheme.Px(24);
            var y = UiTheme.Px(80);
            foreach (var card in new Control[] { _rolling, _weekly, _monthly, _local })
            {
                card.Width = UiTheme.Px(392);
                card.Location = new Point(x, y);
                y += card.Height + UiTheme.Px(16);
            }
            y -= UiTheme.Px(16);
            if (!string.IsNullOrEmpty(_detailStatus.Text))
            {
                _detailStatus.Location = new Point(x, y + UiTheme.Px(16));
                contentBottom = y + UiTheme.Px(16) + _detailStatus.Height;
            }
            else
            {
                contentBottom = y;
            }
        }
        else
        {
            // Codex / GLM 内容宿主按自身内容高度精确设界：永不与顶部状态行/底部按钮重叠
            var top = UiTheme.Px(80);
            var view = _page == 1 ? (Control)_codexView : _glmView;
            var height = _page == 1 ? _codexView.ContentHeight : _glmView.ContentHeight;
            view.Location = new Point(0, top);
            view.Size = new Size(ClientSize.Width, Math.Max(UiTheme.Px(96), height));
            contentBottom = top + height;
        }

        var buttonsY = contentBottom + UiTheme.Px(16);
        var groupWidth = _btnRefresh.Width + UiTheme.Px(8) + _btnKeys.Width + UiTheme.Px(8) + _btnHide.Width;
        var groupX = (ClientSize.Width - groupWidth) / 2;
        _btnRefresh.Location = new Point(groupX, buttonsY);
        _btnKeys.Location = new Point(groupX + _btnRefresh.Width + UiTheme.Px(8), buttonsY);
        _btnHide.Location = new Point(groupX + _btnRefresh.Width + UiTheme.Px(8) + _btnKeys.Width + UiTheme.Px(8), buttonsY);

        var oldSize = Size;
        ClientSize = new Size(UiTheme.Px(440), buttonsY + UiTheme.Px(32) + UiTheme.Px(24));
        ApplyPlacement(oldSize);
    }

    /// <summary>尺寸变化后以底边为锚（展开向上长、收起向下落），并确保整窗不越出工作区。</summary>
    private void ApplyPlacement(Size oldSize)
    {
        if (!IsHandleCreated) return;
        var area = Screen.FromControl(this).WorkingArea;
        var target = WindowPlacement.AnchorBottom(Location, oldSize, Size, area);
        if (target != Location)
            Location = target;
    }

    /// <summary>托盘菜单「退出」调用，跳过“隐藏到托盘”。</summary>
    public void ForceClose()
    {
        _hideOnClose = false;
        Close();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_hideOnClose && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnFormClosing(e);
    }

    /// <summary>仅用于 --uipreview：展开「本机 Token 统计」卡，检查其布局。</summary>
    public void ExpandLocalForPreview()
    {
        _local.SetExpanded(true);
        Relayout();
    }

    /// <summary>仅用于 --uipreview：切到 Codex 页并注入样例状态。</summary>
    public void ShowCodexPageForPreview(CodexState state)
    {
        _codexState = state;
        _pages.SelectedIndex = 1;
        SetPage(1);
    }

    /// <summary>仅用于 --uipreview：切到 GLM 页并注入样例状态。</summary>
    public void ShowGlmPageForPreview(GlmState state)
    {
        _glmState = state;
        _pages.SelectedIndex = 2;
        SetPage(2);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _tips.Dispose();
        base.Dispose(disposing);
    }

    /// <summary>
    /// 官方余量卡：仅展示 /zen/go/v1/usage 返回的 剩余百分比 / 状态 / 重置倒计时。
    /// 元素顺序固定：标题（左）→ 百分比（右，独立占位）→ 进度条 → 状态/重置行。
    /// 本机 Token 统计不在此卡展示（见 LocalTokensCard）。
    /// </summary>
    private sealed class UsageCard : CardPanel
    {
        private readonly Label _name;
        private readonly PercentText _percent;
        private readonly Label _status;
        private readonly FlatProgressBar _bar;

        public UsageCard(string name)
        {
            _name = new Label
            {
                Text = name,
                AutoSize = true,
                BackColor = Color.Transparent,
                Font = UiTheme.Body,
                Location = new Point(UiTheme.Px(16), UiTheme.Px(12)),
            };
            UiTheme.Bind(_name, () => UiTheme.TextPrimary);
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

            Controls.AddRange(new Control[] { _name, _percent, _bar, _status });
            Height = UiTheme.Px(96);
        }

        public void Update(UsageWindow? window, bool stale)
        {
            if (window is null)
            {
                _bar.Set(0, UiTheme.Gray);
                _percent.SetText("接口未返回", UiTheme.Gray);
                _status.Text = string.Empty;
                return;
            }

            var remaining = window.RemainingPercent;
            var color = stale ? UiTheme.Gray : UiTheme.Green;
            _bar.Set(remaining ?? 0, color);

            if (remaining is null)
            {
                _percent.SetText("未知", UiTheme.Gray);
            }
            else
            {
                _percent.Set(DisplayFormat.FormatRemainingNumber(remaining), "%", color);
            }

            _status.Text = DisplayFormat.FormatStatus(window.Status)
                + " · " + DisplayFormat.FormatCountdown(window.ResetsAt, DateTimeOffset.UtcNow);
        }
    }
}
