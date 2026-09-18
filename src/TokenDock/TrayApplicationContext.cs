using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace TokenDock;

/// <summary>托盘常驻主逻辑：60 秒定时刷新、悬停余量、低余量提醒与右键菜单。</summary>
internal sealed class TrayApplicationContext : ApplicationContext
{
    private const int RefreshIntervalMs = 60_000;

    /// <summary>低余量提醒阈值：任一窗口剩余百分比降到该值及以下时气泡提醒一次。</summary>
    private const double LowRemainingThreshold = 20.0;

    private readonly UsageApiClient _client = new();
    private readonly AppState _state = new();
    private readonly CodexAppServerClient _codex = new();
    private readonly CodexState _codexState = new();
    private readonly GlmUsageClient _glmClient = new();
    private readonly GlmState _glmState = new();
    private GlmProvider _glmProvider = GlmSettingsStore.LoadProvider();
    private readonly FloatingOverlaySettings _overlaySettings = FloatingOverlayStore.Load();
    private FloatingOverlayForm? _overlay;
    private System.Windows.Forms.Timer _loginTimer;
    private TokenUsageReport _tokens = TokenUsageReport.Unavailable("尚未采集");
    private SynchronizationContext? _ui;
    private bool _codexRefreshing;
    private bool _glmRefreshing;
    private bool _codexActivated;
    private bool _tokenScanning;
    private int _loginTicks;
    private readonly NotifyIcon _tray;
    private readonly ContextMenuStrip _menu;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly Dictionary<string, bool> _lowNotified = new();
    private DetailForm? _detail;
    private bool _refreshing;
    private bool _exiting;

    public TrayApplicationContext()
    {
        _ui = SynchronizationContext.Current;

        // Codex 事件来自后台线程：统一切回 UI 线程处理
        _codex.RateLimitsUpdated += _ => _ui?.Post(_ => RefreshCodexNow(), null);
        _codex.ProcessExited += () => _ui?.Post(_ =>
        {
            if (_exiting) return;
            CodexStatePolicy.ApplyFailure(_codexState, "Codex 进程已退出");
            _detail?.UpdateCodexState(_codexState);
        }, null);

        _loginTimer = new System.Windows.Forms.Timer { Interval = 3000 };
        _loginTimer.Tick += async (_, _) => await PollCodexLoginAsync();

        var menu = new ContextMenuStrip();
        menu.Items.Add("显示主窗口", null, (_, _) => ShowDetails());
        menu.Items.Add("立即刷新", null, (_, _) => RefreshNow());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => ExitApp());
        TrayMenuTheme.Apply(menu);
        _menu = menu;

        _tray = new NotifyIcon
        {
            Icon = TrayIconFactory.LoadTrayIcon(),
            Text = "TokenDock · AI 用量助手",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) ShowDetails();
        };

        ApplyToUi();

        _timer = new System.Windows.Forms.Timer { Interval = RefreshIntervalMs };
        _timer.Tick += (_, _) =>
        {
            Appearance.RefreshSystemTheme(); // 跟随系统模式下感知系统主题切换
            RefreshNow();
            _ = RefreshTokensAsync();
            if (_codexActivated
                || _overlaySettings.Enabled && _overlay?.Subscription == OverlaySubscription.Codex)
                RefreshCodexNow();
            RefreshGlmNow();
        };
        _timer.Start();

        // 外观（主题 / 毛玻璃 / 透明度）变化：立即重推界面状态，无需重启
        UiTheme.Changed += OnThemeChanged;

        _ = RefreshTokensAsync();

        if (!SecureKeyStore.HasKey)
            OpenSettings();
        RefreshNow();
        if (_overlaySettings.Enabled)
            ShowOverlay();
    }

    private void ShowDetails()
    {
        if (_detail is null || _detail.IsDisposed)
        {
            _detail = new DetailForm(_state, RefreshNow, OpenSettings);
            _detail.CodexLoginRequested += OnCodexLoginRequested;
            _detail.CodexRefreshRequested += RefreshCodexNow;
            _detail.GlmRefreshRequested += RefreshGlmNow;
            _detail.OverlayToggleRequested += ToggleOverlay;
            _detail.GlmSettingsRequested += OpenGlmSettings;
            _detail.PageChanged += page =>
            {
                if (page == 1)
                {
                    _codexActivated = true;
                    RefreshCodexNow();
                }
                else if (page == 2)
                {
                    RefreshGlmNow();
                }
            };
        }

        _detail.UpdateLocalTokens(_tokens);
        _detail.UpdateGlmState(_glmState);

        if (!_detail.Visible)
        {
            var wa = Screen.PrimaryScreen!.WorkingArea;
            _detail.Location = new Point(wa.Right - _detail.Width - 16, wa.Bottom - _detail.Height - 16);
            _detail.Show();
        }
        else
        {
            if (_detail.WindowState == FormWindowState.Minimized)
                _detail.WindowState = FormWindowState.Normal;
            _detail.Activate();
        }

        _detail.UpdateState(_state);
        RefreshNow();
    }

    // ---- 悬浮余量 ----

    private void ToggleOverlay()
    {
        if (_overlay is { Visible: true })
            HideOverlay();
        else
            ShowOverlay();
    }

    private FloatingOverlayForm CreateOverlay()
    {
        var overlay = new FloatingOverlayForm(_overlaySettings);
        overlay.RefreshRequested += () =>
        {
            switch (overlay.Subscription)
            {
                case OverlaySubscription.Codex: RefreshCodexNow(); break;
                case OverlaySubscription.Glm: RefreshGlmNow(); break;
                default: RefreshNow(); break;
            }
        };
        overlay.OpenMainRequested += ShowDetails;
        overlay.HideRequested += HideOverlay;
        overlay.ExitRequested += HideOverlay;
        overlay.SubscriptionChanged += sub =>
        {
            _overlaySettings.Subscription = sub;
            FloatingOverlayStore.Save(_overlaySettings);
            if (sub == OverlaySubscription.Codex) RefreshCodexNow();
            if (sub == OverlaySubscription.Glm) RefreshGlmNow();
        };
        overlay.OpacityChanged += _ => CommitOverlayState();
        overlay.LockChanged += _ => CommitOverlayState();
        overlay.MoveCommitted += CommitOverlayState;
        return overlay;
    }

    private void ShowOverlay()
    {
        _overlay ??= CreateOverlay();
        _overlay.ShowAt(_overlaySettings);
        _overlay.SetStates(_state, _codexState, _glmState);
        _overlaySettings.Enabled = true;
        FloatingOverlayStore.Save(_overlaySettings);
        _detail?.SetOverlayActive(true);
        // 立即补一次当前订阅刷新，避免悬浮窗停留在旧数据
        if (_overlay.Subscription == OverlaySubscription.Codex) RefreshCodexNow();
        if (_overlay.Subscription == OverlaySubscription.Glm) RefreshGlmNow();
    }

    private void HideOverlay()
    {
        _overlay?.Hide();
        _overlaySettings.Enabled = false;
        FloatingOverlayStore.Save(_overlaySettings);
        _detail?.SetOverlayActive(false);
    }

    /// <summary>悬浮窗位置 / 订阅 / 透明度 / 锁定任一变化后整体落盘。</summary>
    private void CommitOverlayState()
    {
        if (_overlay is { } overlay)
        {
            _overlaySettings.X = overlay.Location.X;
            _overlaySettings.Y = overlay.Location.Y;
            _overlaySettings.Subscription = overlay.Subscription;
            _overlaySettings.Locked = overlay.Locked;
            _overlaySettings.Opacity = overlay.Opacity;
        }

        FloatingOverlayStore.Save(_overlaySettings);
    }

    private void PushOverlay()
    {
        _overlay?.SetStates(_state, _codexState, _glmState);
    }

    private void OpenSettings()
    {
        using var form = new SettingsForm(_client, SecureKeyStore.Load());
        if (form.ShowDialog(_detail is { Visible: true } ? _detail : null) == DialogResult.OK)
        {
            _lowNotified.Clear();
            RefreshNow();
        }
    }

    /// <summary>
    /// 本机 Token 采集：只读扫描各客户端会话库（按客户端独立，不合并为账号总量），
    /// 后台线程执行；失败保留上次结果。
    /// </summary>
    private async Task RefreshTokensAsync()
    {
        if (_tokenScanning || _exiting) return;
        _tokenScanning = true;
        try
        {
            var report = await Task.Run(TokenUsageReader.Scan);
            if (_exiting) return;
            _tokens = report;
            _detail?.UpdateLocalTokens(_tokens);
        }
        catch (Exception)
        {
            // 采集异常时保留上次结果，不打断主功能（诊断请用 --tokenscheck）
        }
        finally
        {
            _tokenScanning = false;
        }
    }

    /// <summary>手动或定时触发刷新；进行中的刷新不会重复触发。</summary>
    private async void RefreshNow()
    {
        if (_refreshing || _exiting) return;
        _refreshing = true;
        _detail?.SetRefreshing(true);
        try
        {
            var result = await _client.FetchAsync(SecureKeyStore.Load());
            if (_exiting) return;
            _state.Apply(result);
            if (result.Success && result.Data is not null)
                CheckLowRemaining(result.Data);
            ApplyToUi();
            PushOverlay();
        }
        catch (OperationCanceledException)
        {
            // 程序退出导致的取消，忽略
        }
        catch (Exception ex)
        {
            if (_exiting) return;
            _state.Apply(FetchResult.Fail(FetchFailureKind.NetworkError, "刷新过程出现异常：" + ex.Message));
            ApplyToUi();
        }
        finally
        {
            _refreshing = false;
            _detail?.SetRefreshing(false);
        }
    }

    /// <summary>
    /// GLM 页刷新：远端 Coding Plan 账号统计（额度 / MCP / 模型 Token），
    /// 未配置密钥时只更新状态、不发请求；失败保留旧数据并标记过期。
    /// </summary>
    private async void RefreshGlmNow()
    {
        if (_glmRefreshing || _exiting) return;
        _glmRefreshing = true;
        _detail?.SetRefreshing(true);
        try
        {
            var key = SecureKeyStore.LoadGlm(_glmProvider);
            var result = key is null
                ? GlmFetchResult.Fail(FetchFailureKind.NotConfigured,
                    $"尚未设置 {GlmEndpoints.DisplayName(_glmProvider)} 密钥，点击下方「GLM 密钥」配置。")
                : await _glmClient.FetchAsync(_glmProvider, key);
            if (_exiting) return;
            _glmState.Apply(result);
        }
        catch (OperationCanceledException)
        {
            // 退出导致的取消，忽略
        }
        catch (Exception ex)
        {
            if (_exiting) return;
            _glmState.Apply(GlmFetchResult.Fail(FetchFailureKind.NetworkError, "刷新过程出现异常：" + ex.Message));
        }
        finally
        {
            _glmRefreshing = false;
            _detail?.SetRefreshing(false);
            _detail?.UpdateGlmState(_glmState);
            PushOverlay();
        }
    }

    /// <summary>打开 GLM 设置（Provider / 密钥）；保存成功后立即刷新。</summary>
    private void OpenGlmSettings()
    {
        using var form = new GlmSettingsForm(_glmClient);
        if (form.ShowDialog(_detail is { Visible: true } ? _detail : null) == DialogResult.OK)
        {
            _glmProvider = GlmSettingsStore.LoadProvider();
            RefreshGlmNow();
        }
    }

    /// <summary>
    /// Codex 页刷新：检测依赖 → 登录状态 → 额度读取。失败保留旧数据并标记过期；
    /// 从不回退为满额、不按百分比推算金额。
    /// </summary>
    private async void RefreshCodexNow()
    {
        if (_codexRefreshing || _exiting) return;
        _codexRefreshing = true;
        _detail?.SetRefreshing(true);
        try
        {
            await _codex.EnsureReadyAsync();
            var account = await _codex.AccountReadAsync();
            if (!account.LoggedIn)
            {
                CodexStatePolicy.ApplyNotLoggedIn(_codexState, account);
            }
            else
            {
                var snapshot = await _codex.RateLimitsReadAsync();
                CodexStatePolicy.ApplySnapshot(_codexState, account, snapshot);
            }
        }
        catch (CodexException ex)
        {
            if (ex.Kind == CodexFailureKind.NotInstalled)
                CodexStatePolicy.ApplyNotInstalled(_codexState);
            else
                CodexStatePolicy.ApplyFailure(_codexState, ex.Message);
        }
        catch (Exception ex)
        {
            CodexStatePolicy.ApplyFailure(_codexState, "刷新过程出现异常：" + ex.Message);
        }
        finally
        {
            _codexRefreshing = false;
            _detail?.SetRefreshing(false);
            _detail?.UpdateCodexState(_codexState);
            PushOverlay();
        }
    }

    /// <summary>
    /// 登录：account/login/start（type=chatgpt）→ 打开返回的授权网址 → 轮询等待登录完成。
    /// 不要求 API Key；令牌由 Codex 本机保管，本程序不读取、不记录、不保存。
    /// </summary>
    private async void OnCodexLoginRequested()
    {
        if (_codexRefreshing || _exiting) return;
        _codexActivated = true;
        _codexRefreshing = true;
        _detail?.SetRefreshing(true);
        try
        {
            await _codex.EnsureReadyAsync();
            var (_, authUrl) = await _codex.LoginStartAsync();
            _codexState.LoginInProgress = true;
            _loginTicks = 0;
            Process.Start(new ProcessStartInfo(authUrl!) { UseShellExecute = true });
            _loginTimer.Start();
        }
        catch (CodexException ex)
        {
            if (ex.Kind == CodexFailureKind.NotInstalled)
                CodexStatePolicy.ApplyNotInstalled(_codexState);
            else
                CodexStatePolicy.ApplyFailure(_codexState, "登录启动失败：" + ex.Message);
        }
        catch (Exception ex)
        {
            CodexStatePolicy.ApplyFailure(_codexState, "登录启动失败：" + ex.Message);
        }
        finally
        {
            _codexRefreshing = false;
            _detail?.SetRefreshing(false);
            _detail?.UpdateCodexState(_codexState);
        }
    }

    /// <summary>登录轮询：每 3 秒检查一次登录状态，成功即拉取额度；最长约 5 分钟。</summary>
    private async Task PollCodexLoginAsync()
    {
        if (_exiting)
        {
            _loginTimer.Stop();
            return;
        }

        if (++_loginTicks > 100)
        {
            _loginTicks = 0;
            _loginTimer.Stop();
            _codexState.LoginInProgress = false;
            CodexStatePolicy.ApplyFailure(_codexState, "登录等待超时，请重试");
            _detail?.UpdateCodexState(_codexState);
            return;
        }

        try
        {
            var account = await _codex.AccountReadAsync();
            if (!account.LoggedIn) return;

            _loginTicks = 0;
            _loginTimer.Stop();
            _codexState.LoginInProgress = false;
            var snapshot = await _codex.RateLimitsReadAsync();
            CodexStatePolicy.ApplySnapshot(_codexState, account, snapshot);
        }
        catch (Exception)
        {
            // 轮询期间失败静默重试，由超时兜底
        }
        finally
        {
            _detail?.UpdateCodexState(_codexState);
        }
    }

    private void CheckLowRemaining(UsageData data)
    {
        CheckOne("rolling", "5小时窗口", data.Rolling);
        CheckOne("weekly", "本周", data.Weekly);
        CheckOne("monthly", "本月", data.Monthly);
    }

    private void CheckOne(string key, string label, UsageWindow? window)
    {
        if (window is null) return;
        var remaining = window.RemainingPercent;
        if (remaining is null) return;

        bool isLow = remaining.Value <= LowRemainingThreshold;
        bool wasLow = _lowNotified.TryGetValue(key, out var prev) && prev;
        _lowNotified[key] = isLow;

        if (isLow && !wasLow)
        {
            _tray.ShowBalloonTip(5000, "套餐余量不足",
                $"{label}仅剩 {DisplayFormat.FormatRemainingPercent(remaining)}，"
                + $"{DisplayFormat.FormatCountdown(window.ResetsAt, DateTimeOffset.UtcNow)}。",
                ToolTipIcon.Warning);
        }
    }

    /// <summary>外观变化：托盘菜单重取色 + 主窗（含 Codex / GLM / 本机统计）按新调色板重推数据。</summary>
    private void OnThemeChanged()
    {
        if (_exiting) return;
        TrayMenuTheme.Refresh(_menu);
        ApplyToUi();
        _detail?.UpdateCodexState(_codexState);
        _detail?.UpdateGlmState(_glmState);
        _detail?.UpdateLocalTokens(_tokens);
        PushOverlay();
    }

    private void ApplyToUi()
    {
        if (_exiting) return;
        try
        {
            _tray.Text = BuildTooltip();
            _detail?.UpdateState(_state);
        }
        catch (ObjectDisposedException)
        {
            // 窗口 / 托盘正在关闭
        }
    }

    private string BuildTooltip()
    {
        if (_state.LastGood is null)
        {
            var reason = _state.LastFailureKind switch
            {
                FetchFailureKind.None => "尚未获取数据",
                FetchFailureKind.NotConfigured => "未设置密钥",
                _ => "获取失败",
            };
            return TruncateTooltip($"AI 用量助手（{reason}）");
        }

        var data = _state.LastGood;
        static string Part(UsageWindow? w) => DisplayFormat.FormatRemainingPercent(w?.RemainingPercent);
        var prefix = _state.IsStale ? "数据过期，" : "";
        return TruncateTooltip(
            $"AI 用量助手（{prefix}5小时 {Part(data.Rolling)}｜周 {Part(data.Weekly)}｜月 {Part(data.Monthly)}）");
    }

    private static string TruncateTooltip(string text) => text.Length <= 63 ? text : text[..63];

    private void ExitApp()
    {
        _exiting = true;
        _timer.Stop();
        _overlay?.Hide();
        _tray.Visible = false;
        _detail?.ForceClose();
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _exiting = true;
            UiTheme.Changed -= OnThemeChanged;
            _timer.Stop();
            _timer.Dispose();
            _loginTimer.Stop();
            _loginTimer.Dispose();
            _codex.Dispose();
            _glmClient.Dispose();
            _tray.Visible = false;
            _tray.Dispose();
            _client.Dispose();
            _detail?.Dispose();
        }
        base.Dispose(disposing);
    }
}
