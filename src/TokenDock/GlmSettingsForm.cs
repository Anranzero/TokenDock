using System.Drawing;
using System.Text.Json;
using System.Windows.Forms;

namespace TokenDock;

/// <summary>GLM 设置存储（非敏感）：选中的 Provider。密钥单独走 DPAPI 加密文件。</summary>
internal static class GlmSettingsStore
{
    private static string FilePath => Path.Combine(AppDataPaths.Directory, "glm.json");

    public static GlmProvider LoadProvider()
    {
        try
        {
            if (!File.Exists(FilePath)) return GlmProvider.Zai;
            using var doc = JsonDocument.Parse(File.ReadAllText(FilePath));
            if (doc.RootElement.TryGetProperty("provider", out var p) && p.ValueKind == JsonValueKind.String
                && string.Equals(p.GetString(), "bigmodel", StringComparison.OrdinalIgnoreCase))
                return GlmProvider.BigModel;
        }
        catch (Exception)
        {
            // 缺失 / 损坏：默认 Z.ai
        }

        return GlmProvider.Zai;
    }

    public static void SaveProvider(GlmProvider provider)
    {
        try
        {
            Directory.CreateDirectory(AppDataPaths.Directory);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(
                new { provider = provider == GlmProvider.Zai ? "zai" : "bigmodel" },
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception)
        {
            // 落盘失败不影响本次运行
        }
    }
}

/// <summary>
/// GLM Coding Plan 设置窗（Win11 原生标题栏紧凑弹窗）：
/// 选择 Provider（Z.ai 国际版 / BigModel 国内版，Base URL 与密钥各自独立）→ 填 API Key →
/// 「保存并验证」用真实额度接口验证后才写入 DPAPI 加密存储。
/// </summary>
internal sealed class GlmSettingsForm : Form
{
    private readonly GlmUsageClient _client;
    private readonly SegmentedControl _providerChoice;
    private readonly TextBox _txtKey = new();
    private readonly EyeToggle _eye = new();
    private readonly CardPanel _keyInput;
    private readonly Label _hint;
    private readonly LinkButton _link;
    private readonly PillButton _btnSave = new(primary: true);
    private readonly PillButton _btnCancel = new();
    private GlmProvider _provider;
    private bool _initializing;

    public GlmSettingsForm(GlmUsageClient client)
    {
        _client = client;
        _provider = GlmSettingsStore.LoadProvider();

        Text = "GLM 设置";
        try { Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!); } catch (ArgumentException) { }
        Font = UiTheme.Body;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.None;
        ClientSize = new Size(UiTheme.Px(440), UiTheme.Px(288));
        Appearance.Attach(this);

        var title = new Label
        {
            Text = "GLM Coding Plan",
            AutoSize = true,
            BackColor = Color.Transparent,
            Font = UiTheme.Title,
            Location = new Point(UiTheme.Px(24), UiTheme.Px(24)),
        };
        UiTheme.Bind(title, () => UiTheme.TextPrimary);

        var providerCaption = new Label
        {
            Text = "Provider",
            AutoSize = true,
            BackColor = Color.Transparent,
            Font = UiTheme.Tiny,
            Location = new Point(UiTheme.Px(24), UiTheme.Px(68)),
        };
        UiTheme.Bind(providerCaption, () => UiTheme.TextSecondary);
        _providerChoice = new SegmentedControl("Z.ai 国际", "BigModel 国内")
        {
            Size = new Size(UiTheme.Px(240), UiTheme.Px(28)),
            Location = new Point(UiTheme.Px(112), UiTheme.Px(62)),
        };
        _providerChoice.SelectionChanged += index =>
        {
            if (_initializing) return;
            _provider = index == 1 ? GlmProvider.BigModel : GlmProvider.Zai;
            GlmSettingsStore.SaveProvider(_provider);
            LoadProviderState();
        };

        var keyCaption = new Label
        {
            Text = "API Key",
            AutoSize = true,
            BackColor = Color.Transparent,
            Font = UiTheme.Tiny,
            Location = new Point(UiTheme.Px(24), UiTheme.Px(108)),
        };
        UiTheme.Bind(keyCaption, () => UiTheme.TextSecondary);
        _keyInput = new CardPanel(8f)
        {
            Location = new Point(UiTheme.Px(112), UiTheme.Px(100)),
            Size = new Size(UiTheme.Px(304), UiTheme.Px(32)),
        };
        _keyInput.BorderSource = () => UiTheme.InputBorder;
        _txtKey.BorderStyle = BorderStyle.None;
        _txtKey.Font = UiTheme.Body;
        _txtKey.Location = new Point(UiTheme.Px(10), UiTheme.Px(8));
        _txtKey.Size = new Size(UiTheme.Px(258), UiTheme.Px(16));
        _txtKey.UseSystemPasswordChar = true;
        UiTheme.BindBack(_txtKey, () => UiTheme.Card);
        UiTheme.Bind(_txtKey, () => UiTheme.TextPrimary);
        _txtKey.GotFocus += (_, _) => _keyInput.BorderSource = () => UiTheme.Green;
        _txtKey.LostFocus += (_, _) => _keyInput.BorderSource = () => UiTheme.InputBorder;
        _txtKey.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                OnSaveClick(this, EventArgs.Empty);
            }
        };

        _eye.Size = new Size(UiTheme.Px(20), UiTheme.Px(20));
        _eye.Location = new Point(UiTheme.Px(276), UiTheme.Px(6));
        _eye.Toggled += () =>
        {
            _txtKey.UseSystemPasswordChar = !_txtKey.UseSystemPasswordChar;
            _eye.Revealed = !_txtKey.UseSystemPasswordChar;
            _txtKey.Focus();
        };

        _hint = new Label
        {
            AutoSize = false,
            BackColor = Color.Transparent,
            Font = UiTheme.Tiny,
            Size = new Size(UiTheme.Px(392), UiTheme.Px(32)),
            Location = new Point(UiTheme.Px(24), UiTheme.Px(144)),
        };
        UiTheme.Bind(_hint, () => UiTheme.TextSecondary);

        _link = new LinkButton
        {
            Text = "打开密钥管理页",
            Size = new Size(UiTheme.Px(120), UiTheme.Px(20)),
            Location = new Point(UiTheme.Px(24), UiTheme.Px(176)),
        };
        _link.Click += (_, _) =>
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                    GlmEndpoints.ApiKeyHelpUrl(_provider)) { UseShellExecute = true });
            }
            catch (Exception)
            {
                // 打不开浏览器时忽略（URL 已显示在提示文字里）
            }
        };

        var note = new Label
        {
            AutoSize = false,
            BackColor = Color.Transparent,
            Font = UiTheme.Tiny,
            Size = new Size(UiTheme.Px(392), UiTheme.Px(16)),
            Text = "密钥按 Provider 分开保存（DPAPI 加密，仅本机），不会出现在日志里。",
            Location = new Point(UiTheme.Px(24), UiTheme.Px(204)),
        };
        UiTheme.Bind(note, () => UiTheme.TextSecondary);

        _btnCancel.Text = "取消";
        _btnCancel.Size = new Size(UiTheme.Px(88), UiTheme.Px(32));
        _btnCancel.DialogResult = DialogResult.Cancel;
        _btnSave.Text = "保存并验证";
        _btnSave.Size = new Size(UiTheme.Px(120), UiTheme.Px(32));
        _btnSave.Click += OnSaveClick;
        AcceptButton = _btnSave;
        CancelButton = _btnCancel;

        _keyInput.Controls.Add(_txtKey);
        _keyInput.Controls.Add(_eye);
        Controls.AddRange(new Control[]
        {
            title, providerCaption, _providerChoice, keyCaption, _keyInput, _eye,
            _hint, _link, note, _btnSave, _btnCancel,
        });

        LoadProviderState();
        _initializing = true;
        _providerChoice.SelectedIndex = _provider == GlmProvider.BigModel ? 1 : 0;
        _initializing = false;
    }

    /// <summary>切换 Provider 时回填该 Provider 自己的密钥（互不混用）。</summary>
    private void LoadProviderState()
    {
        var key = SecureKeyStore.LoadGlm(_provider) ?? string.Empty;
        _txtKey.Text = key;
        _txtKey.UseSystemPasswordChar = true;
        _eye.Revealed = false;
        _hint.Text = $"{GlmEndpoints.DisplayName(_provider)} · Base URL：{GlmEndpoints.BaseUrl(_provider)}\r\n"
            + $"密钥管理页：{GlmEndpoints.ApiKeyHelpUrl(_provider)}"
            + (SecureKeyStore.HasGlmKey(_provider) ? "（已保存密钥，可留空沿用）" : "");
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _txtKey.SelectionStart = _txtKey.TextLength;
        _txtKey.SelectionLength = 0;
        BeginInvoke(() =>
        {
            _txtKey.SelectionStart = _txtKey.TextLength;
            _txtKey.SelectionLength = 0;
            CenterButtons();
        });
    }

    private void CenterButtons()
    {
        var groupW = _btnSave.Width + UiTheme.Px(8) + _btnCancel.Width;
        var groupX = Math.Max(UiTheme.Px(24), (ClientSize.Width - groupW) / 2);
        _btnSave.Location = new Point(groupX, ClientSize.Height - UiTheme.Px(24) - UiTheme.Px(32));
        _btnCancel.Location = new Point(groupX + groupW - _btnCancel.Width, _btnSave.Top);
    }

    private async void OnSaveClick(object? sender, EventArgs e)
    {
        var key = _txtKey.Text.Trim();
        if (key.Length == 0)
        {
            MessageBox.Show(this, "请输入 API Key。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _btnSave.Enabled = false;
        _btnCancel.Enabled = false;
        UseWaitCursor = true;
        try
        {
            var result = await _client.FetchAsync(_provider, key);
            if (result.Success)
            {
                SecureKeyStore.SaveGlm(_provider, key);
                GlmSettingsStore.SaveProvider(_provider);
                MessageBox.Show(this,
                    $"{GlmEndpoints.DisplayName(_provider)} 密钥验证成功，已加密保存到本机。",
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
