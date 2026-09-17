using System.Drawing;
using System.Windows.Forms;

namespace TokenDock;

/// <summary>
/// 设置窗口（Win11 原生标题栏紧凑弹窗，8px 栅格、全部自绘控件）：
/// API 密钥（先验证后保存）+ 外观（浅色 / 暗色 / 跟随系统、普通 / 毛玻璃、透明度、动画）。
/// 外观改动即时预览并落盘，无需重启。
/// </summary>
internal sealed class SettingsForm : Form
{
    private readonly UsageApiClient _client;
    private readonly TextBox _txtKey = new();
    private readonly EyeToggle _eye = new();
    private readonly PillButton _btnSave = new(primary: true);
    private readonly PillButton _btnCancel = new();
    private readonly CardPanel _keyInput;
    private readonly SegmentedControl _themeChoice;
    private readonly SegmentedControl _effectChoice;
    private readonly SliderRow _opacity;
    private readonly CheckRow _animations;
    private bool _initializing;

    public SettingsForm(UsageApiClient client, string? existingKey)
    {
        _client = client;

        Text = "设置";
        try { Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!); } catch (ArgumentException) { }
        Font = UiTheme.Body;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.None;
        ClientSize = new Size(UiTheme.Px(440), UiTheme.Px(392));
        Appearance.Attach(this);

        // ---- API 密钥 ----
        var keyTitle = new Label
        {
            Text = "API 密钥",
            AutoSize = true,
            BackColor = Color.Transparent,
            Font = UiTheme.Title,
            Location = new Point(UiTheme.Px(24), UiTheme.Px(24)),
        };
        UiTheme.Bind(keyTitle, () => UiTheme.TextPrimary);
        var desc = new Label
        {
            Text = "OpenCode 控制台生成的 API Key；DPAPI 加密后仅保存在本机，不上传第三方。",
            AutoSize = true,
            BackColor = Color.Transparent,
            Font = UiTheme.Tiny,
            Location = new Point(UiTheme.Px(24), UiTheme.Px(52)),
        };
        UiTheme.Bind(desc, () => UiTheme.TextSecondary);

        _keyInput = new CardPanel(8f)
        {
            Location = new Point(UiTheme.Px(24), UiTheme.Px(84)),
            Size = new Size(UiTheme.Px(392), UiTheme.Px(32)),
        };
        _keyInput.BorderSource = () => UiTheme.InputBorder; // 随主题取色（聚焦时切换为绿色）
        _txtKey.BorderStyle = BorderStyle.None;
        _txtKey.Font = UiTheme.Body;
        _txtKey.Location = new Point(UiTheme.Px(10), UiTheme.Px(8));
        _txtKey.Size = new Size(UiTheme.Px(346), UiTheme.Px(16));
        _txtKey.UseSystemPasswordChar = true;
        _txtKey.Text = existingKey ?? string.Empty;
        UiTheme.BindBack(_txtKey, () => UiTheme.Card);
        UiTheme.Bind(_txtKey, () => UiTheme.TextPrimary);
        _txtKey.GotFocus += (_, _) => _keyInput.BorderSource = () => UiTheme.Green;
        _txtKey.LostFocus += (_, _) => _keyInput.BorderSource = () => UiTheme.InputBorder;

        // 眼睛图标放在输入框内右侧：睁眼=明文，闭眼=密码
        _eye.Size = new Size(UiTheme.Px(20), UiTheme.Px(20));
        _eye.Location = new Point(UiTheme.Px(364), UiTheme.Px(6));
        _eye.Toggled += () =>
        {
            _txtKey.UseSystemPasswordChar = !_txtKey.UseSystemPasswordChar;
            _eye.Revealed = !_txtKey.UseSystemPasswordChar;
            _txtKey.Focus();
        };

        // 主操作按钮组整组水平居中（OnShown 时按最终客户区尺寸精确计算）
        _btnCancel.Text = "取消";
        _btnCancel.Size = new Size(UiTheme.Px(88), UiTheme.Px(32));
        _btnCancel.DialogResult = DialogResult.Cancel;
        _btnSave.Text = "保存并验证";
        _btnSave.Size = new Size(UiTheme.Px(120), UiTheme.Px(32));
        _btnSave.Location = new Point(UiTheme.Px(112), UiTheme.Px(132));
        _btnCancel.Location = new Point(UiTheme.Px(240), UiTheme.Px(132));
        _btnSave.Click += OnSaveClick;

        // ---- 外观 ----
        var appearanceTitle = new Label
        {
            Text = "外观",
            AutoSize = true,
            BackColor = Color.Transparent,
            Font = UiTheme.Title,
            Location = new Point(UiTheme.Px(24), UiTheme.Px(196)),
        };
        UiTheme.Bind(appearanceTitle, () => UiTheme.TextPrimary);

        var themeCaption = new Label
        {
            Text = "主题",
            AutoSize = true,
            BackColor = Color.Transparent,
            Font = UiTheme.Tiny,
            Location = new Point(UiTheme.Px(24), UiTheme.Px(234)),
        };
        UiTheme.Bind(themeCaption, () => UiTheme.TextSecondary);
        _themeChoice = new SegmentedControl("浅色", "暗色", "跟随系统")
        {
            Size = new Size(UiTheme.Px(240), UiTheme.Px(28)),
            Location = new Point(UiTheme.Px(112), UiTheme.Px(228)),
        };
        _themeChoice.SelectionChanged += index =>
        {
            if (_initializing) return;
            Appearance.Update(s => s.Theme = (ThemeChoice)index);
        };

        var effectCaption = new Label
        {
            Text = "窗口效果",
            AutoSize = true,
            BackColor = Color.Transparent,
            Font = UiTheme.Tiny,
            Location = new Point(UiTheme.Px(24), UiTheme.Px(274)),
        };
        UiTheme.Bind(effectCaption, () => UiTheme.TextSecondary);
        _effectChoice = new SegmentedControl("普通", "毛玻璃")
        {
            Size = new Size(UiTheme.Px(160), UiTheme.Px(28)),
            Location = new Point(UiTheme.Px(112), UiTheme.Px(268)),
        };
        _effectChoice.SelectionChanged += index =>
        {
            if (_initializing) return;
            Appearance.Update(s => s.Effect = (WindowEffectChoice)index);
            UpdateOpacityEnabled();
        };

        _opacity = new SliderRow
        {
            Location = new Point(UiTheme.Px(24), UiTheme.Px(304)),
            Size = new Size(UiTheme.Px(392), UiTheme.Px(32)),
        };
        _opacity.ValueChanged += () =>
        {
            if (_initializing) return;
            Appearance.Update(s => s.GlassOpacity = _opacity.Value / 100.0);
        };

        _animations = new CheckRow("开启动画（进度条缓动等，关闭后立即到位）")
        {
            Location = new Point(UiTheme.Px(24), UiTheme.Px(340)),
            Size = new Size(UiTheme.Px(392), UiTheme.Px(32)),
        };
        _animations.CheckedChanged += () =>
        {
            if (_initializing) return;
            Appearance.Update(s => s.Animations = _animations.Checked);
        };

        AcceptButton = _btnSave;
        CancelButton = _btnCancel;
        _keyInput.Controls.Add(_txtKey);
        _keyInput.Controls.Add(_eye);
        Controls.AddRange(new Control[]
        {
            keyTitle, desc, _keyInput, _btnSave, _btnCancel,
            appearanceTitle, themeCaption, _themeChoice, effectCaption, _effectChoice, _opacity, _animations,
        });

        LoadAppearanceState();
    }

    /// <summary>把当前外观设置回填到控件（用守卫避免触发即时应用回环）。</summary>
    private void LoadAppearanceState()
    {
        _initializing = true;
        try
        {
            var current = Appearance.Current;
            _themeChoice.SelectedIndex = (int)current.Theme;
            _effectChoice.SelectedIndex = (int)current.Effect;
            _opacity.Value = (int)Math.Round(current.GlassOpacity * 100);
            _animations.Checked = current.Animations;
        }
        finally
        {
            _initializing = false;
        }

        UpdateOpacityEnabled();
    }

    /// <summary>透明度只对毛玻璃生效：普通模式下禁用滑杆（视觉置灰，值保留）。</summary>
    private void UpdateOpacityEnabled()
        => _opacity.Enabled = Appearance.Current.Effect == WindowEffectChoice.Glass;

    /// <summary>显示后按最终客户区尺寸把主操作按钮组水平居中，并收起密钥选区。</summary>
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        ClearKeySelection();
        CenterKeyButtons();
        BeginInvoke(() =>
        {
            ClearKeySelection();
            CenterKeyButtons();
        });
    }

    private void CenterKeyButtons()
    {
        var groupW = _btnSave.Width + UiTheme.Px(8) + _btnCancel.Width;
        var groupX = Math.Max(UiTheme.Px(24), (ClientSize.Width - groupW) / 2);
        _btnSave.Location = new Point(groupX, _btnSave.Top);
        _btnCancel.Location = new Point(groupX + groupW - _btnCancel.Width, _btnCancel.Top);
    }

    private void ClearKeySelection()
    {
        _txtKey.SelectionStart = _txtKey.TextLength;
        _txtKey.SelectionLength = 0;
    }

    /// <summary>8px 栅格分隔线（随主题取色）。</summary>
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var brush = new SolidBrush(UiTheme.Hairline);
        e.Graphics.FillRectangle(brush, UiTheme.Px(24), UiTheme.Px(180), ClientSize.Width - UiTheme.Px(48), 1);
    }

    private async void OnSaveClick(object? sender, EventArgs e)
    {
        var key = _txtKey.Text.Trim();
        if (key.Length == 0)
        {
            MessageBox.Show(this, "请输入 API 密钥。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _btnSave.Enabled = false;
        _btnCancel.Enabled = false;
        UseWaitCursor = true;
        try
        {
            var result = await _client.FetchAsync(key);
            // HTTP 200 但格式异常时密钥本身已被服务端接受，同样保存
            if (result.Success || result.FailureKind == FetchFailureKind.ParseError)
            {
                SecureKeyStore.Save(key);
                MessageBox.Show(this,
                    result.Success
                        ? "密钥验证成功，已加密保存到本机。"
                        : "密钥已被服务端接受，但接口返回格式与预期不同，已先保存到本机。",
                    "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                DialogResult = DialogResult.OK;
            }
            else
            {
                var msg = result.ErrorMessage
                    + (string.IsNullOrEmpty(result.ServerDetail) ? "" : $"\r\n服务端信息：{result.ServerDetail}");
                MessageBox.Show(this, msg, "验证失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "验证过程出现异常：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            UseWaitCursor = false;
            _btnSave.Enabled = true;
            _btnCancel.Enabled = true;
        }
    }
}
