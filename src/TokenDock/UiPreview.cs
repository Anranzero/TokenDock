using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace TokenDock;

/// <summary>
/// --uipreview：把详情窗口（官方余量卡 / 本机 Token 统计 / 过期 / 失败 / 未接入）、
/// Codex 页与设置窗口在 浅色 / 暗色 / 毛玻璃 三套外观 × 100% / 125% / 150% 缩放下渲染为 PNG，
/// 供开发期视觉自查（暗色完整性、无浅色残留、布局无重叠），不影响正常功能。
/// </summary>
internal static class UiPreview
{
    private static readonly (string Name, bool Dark, bool Glass)[] Presets =
    {
        ("light", false, false),
        ("dark", true, false),
        ("glass", true, true),
    };

    public static int Run()
    {
        var outDir = Path.Combine(Path.GetTempPath(), "tokendock", "uipreview");
        Directory.CreateDirectory(outDir);

        var failures = 0;
        void Shot(Form form, string name, Action? afterShow = null)
        {
            try
            {
                var wa = Screen.PrimaryScreen!.WorkingArea;
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(wa.Right - form.Width - 8, wa.Bottom - form.Height - 8);
                form.Show();
                afterShow?.Invoke(); // 需要"窗口已可见"的交互（如刷新态）在 Show 之后再触发，贴近真实使用
                // 等进度条入场动画播完再截取，避免截到中间帧
                var deadline = DateTime.UtcNow.AddMilliseconds(800);
                while (DateTime.UtcNow < deadline)
                {
                    Application.DoEvents();
                    Thread.Sleep(15);
                }

                using var bitmap = new Bitmap(form.Width, form.Height);
                form.DrawToBitmap(bitmap, new Rectangle(0, 0, form.Width, form.Height));
                var path = Path.Combine(outDir, name);
                bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                Console.WriteLine("[预览] " + path);
            }
            catch (Exception ex)
            {
                failures++;
                Console.WriteLine("[失败] " + name + "：" + ex.Message);
            }
            finally
            {
                form.Dispose();
            }
        }

        foreach (var (scale, tag) in new[] { (1.0f, "100"), (1.25f, "125"), (1.5f, "150") })
        {
            UiTheme.Init(scale);

            foreach (var (preset, dark, glass) in Presets)
            {
                UiTheme.ApplyPalette(dark, glass);

                var fresh = new AppState();
                fresh.Apply(UsageApiClient.ParseSuccessBody(SampleJson()));

                var okForm = new DetailForm(fresh, () => { }, () => { });
                okForm.UpdateLocalTokens(SampleTokens());
                okForm.ExpandLocalForPreview();
                Shot(okForm, $"detail-ok-{preset}-{tag}.png");
                Shot(new SettingsForm(new UsageApiClient(), null), $"settings-{preset}-{tag}.png");

                var codexForm = new DetailForm(fresh, () => { }, () => { });
                codexForm.ShowCodexPageForPreview(CodexSampleReady());
                Shot(codexForm, $"codex-ok-{preset}-{tag}.png");

                var glmForm = new DetailForm(fresh, () => { }, () => { });
                glmForm.ShowGlmPageForPreview(GlmSampleReady());
                Shot(glmForm, $"glm-ok-{preset}-{tag}.png");

                // 异常状态各主题都要看（状态胶囊 / 错误文案的对比度）
                if (tag == "100" || (tag == "150" && preset != "glass"))
                {
                    var stale = new AppState();
                    stale.Apply(UsageApiClient.ParseSuccessBody(SampleJson()));
                    stale.Apply(FetchResult.Fail(FetchFailureKind.NetworkError, "网络连接失败或超时，请检查网络。"));
                    var staleForm = new DetailForm(stale, () => { }, () => { });
                    staleForm.UpdateLocalTokens(SampleTokens());
                    Shot(staleForm, $"detail-stale-{preset}-{tag}.png");

                    var noData = new AppState();
                    noData.Apply(FetchResult.Fail(FetchFailureKind.NotConfigured, "尚未设置 API 密钥，请右键托盘图标选择「设置密钥」。"));
                    Shot(new DetailForm(noData, () => { }, () => { }), $"detail-error-{preset}-{tag}.png");
                }

                if (tag == "100")
                {
                    // 本机 Token 统计卡「未接入」态（采集不到本机会话数据时显示，而不是 0）
                    var noConnect = new DetailForm(fresh, () => { }, () => { });
                    noConnect.UpdateLocalTokens(TokenUsageReport.Unavailable("未发现本机 OpenCode/Zcode 会话数据"));
                    noConnect.ExpandLocalForPreview();
                    Shot(noConnect, $"detail-noconnect-{preset}-{tag}.png");

                    var codexStale = new DetailForm(fresh, () => { }, () => { });
                    codexStale.ShowCodexPageForPreview(CodexSampleStale());
                    Shot(codexStale, $"codex-stale-{preset}-{tag}.png");

                    var glmNoKey = new DetailForm(fresh, () => { }, () => { });
                    glmNoKey.ShowGlmPageForPreview(GlmSampleNotConfigured());
                    Shot(glmNoKey, $"glm-nokey-{preset}-{tag}.png");
                }

                if (tag == "150" && preset == "light")
                {
                    var codexNotInstalled = new DetailForm(fresh, () => { }, () => { });
                    codexNotInstalled.ShowCodexPageForPreview(CodexSampleNotInstalled());
                    Shot(codexNotInstalled, $"codex-notinstalled-{tag}.png");

                    // 刷新中状态（检查状态胶囊 / 转圈 / 设置齿轮互不重叠）
                    var refreshingForm = new DetailForm(fresh, () => { }, () => { });
                    Shot(refreshingForm, $"detail-refreshing-{tag}.png", () => refreshingForm.SetRefreshing(true));
                }
            }
        }

        UiTheme.ApplyPalette(Appearance.IsDark, Appearance.IsGlass); // 还原本次会话调色板
        Console.WriteLine(failures == 0 ? "界面预览生成完成。" : $"界面预览失败 {failures} 项。");
        return failures == 0 ? 0 : 1;
    }

    /// <summary>
    /// 本机 Token 统计样例（虚构记录，按客户端独立分组，绝不合并为账号总量）：
    /// OpenCode 客户端为较早的本机历史（含超过 6 个模型触发“其他”折叠），
    /// Zcode 客户端为近期记录——模拟“切换客户端后两份数据各自独立”的真实形态。
    /// </summary>
    private static TokenUsageReport SampleTokens()
    {
        var now = DateTimeOffset.UtcNow;
        var records = new List<TokenUsageRecord>();
        void Add(List<TokenUsageRecord> target, string model, double hoursAgo,
            long input, long output, long reasoning, long cacheRead, long cacheWrite)
            => target.Add(new TokenUsageRecord
            {
                MessageId = $"{model}-{hoursAgo}",
                Model = model,
                CreatedUtc = now.AddHours(-hoursAgo),
                Input = input,
                Output = output,
                Reasoning = reasoning,
                CacheRead = cacheRead,
                CacheWrite = cacheWrite,
                Total = input + output + reasoning + cacheRead + cacheWrite,
            });

        var opencodeRecords = new List<TokenUsageRecord>();
        Add(opencodeRecords, "deepseek-v4-flash", 90, 120_000, 8_500, 3_200, 900_000, 0);
        Add(opencodeRecords, "deepseek-v4-flash", 91.5, 90_000, 6_100, 2_100, 700_000, 0);
        Add(opencodeRecords, "glm-5.2", 100, 240_000, 12_400, 0, 1_800_000, 12_000);
        Add(opencodeRecords, "kimi-k3", 30, 380_910, 47_834, 76_000, 11_832_037, 0);
        Add(opencodeRecords, "qwen3.7-plus", 100, 568, 34_004, 0, 6_434_518, 1_097_043);
        Add(opencodeRecords, "deepseek-v4-pro", 200, 1_084_135, 84_116, 50_000, 27_485_696, 0);
        Add(opencodeRecords, "glm-5.3", 400, 171_456, 19_580, 19_200, 4_192_000, 0);
        Add(opencodeRecords, "omen-alpha", 500, 684_664, 63_216, 0, 4_948_800, 0);

        var zcodeRecords = new List<TokenUsageRecord>();
        Add(zcodeRecords, "deepseek-v4.1-flash", 1.0, 45_000, 3_100, 900, 260_000, 0);
        Add(zcodeRecords, "deepseek-v4.1-flash", 3.5, 38_400, 2_600, 700, 210_000, 0);

        ClientTokenUsage Client(string name, string source, List<TokenUsageRecord> list) => new()
        {
            ClientName = name,
            Source = source,
            Available = true,
            DatabaseCount = name == "OpenCode" ? 2 : 1,
            Records = list,
            EarliestUtc = list.Min(r => r.CreatedUtc),
            LatestUtc = list.Max(r => r.CreatedUtc),
        };

        return new TokenUsageReport
        {
            Available = true,
            Detail = "本机客户端会话库只读采集（仅 provider=opencode-go*）",
            Clients = new[]
            {
                Client("OpenCode", "~/.local/share/opencode（2 个会话库）", opencodeRecords),
                Client("Zcode", "~/.zcode/cli/db/db.sqlite", zcodeRecords),
            },
        };
    }

    /// <summary>Codex 页样例（虚构邮箱，绝不使用真实账号信息）。</summary>
    private static CodexState CodexSampleReady()
    {
        var state = new CodexState();
        var snapshot = new CodexRateLimitsSnapshot
        {
            Groups = new[]
            {
                new CodexRateGroup
                {
                    LimitId = "codex",
                    Primary = new CodexRateWindow
                    {
                        UsedPercent = 57,
                        WindowDurationMins = 10080,
                        ResetsAtUnixSeconds = DateTimeOffset.UtcNow.AddHours(48).ToUnixTimeSeconds(),
                    },
                },
                new CodexRateGroup
                {
                    LimitId = "codex_bengalfox",
                    LimitName = "GPT-5.3-Codex-Spark",
                    Primary = new CodexRateWindow
                    {
                        UsedPercent = 0,
                        WindowDurationMins = 300,
                        ResetsAtUnixSeconds = DateTimeOffset.UtcNow.AddHours(4).ToUnixTimeSeconds(),
                    },
                    Secondary = new CodexRateWindow
                    {
                        UsedPercent = 12,
                        WindowDurationMins = 10080,
                        ResetsAtUnixSeconds = DateTimeOffset.UtcNow.AddDays(6).ToUnixTimeSeconds(),
                    },
                },
            },
        };
        CodexStatePolicy.ApplySnapshot(
            state,
            new CodexAccountInfo { LoggedIn = true, Email = "user@example.com", PlanType = "plus" },
            snapshot);
        return state;
    }

    private static CodexState CodexSampleStale()
    {
        var state = CodexSampleReady();
        CodexStatePolicy.ApplyFailure(state, "failed to fetch codex rate limits（代码 -32603）");
        return state;
    }

    /// <summary>GLM 页样例（虚构数据，字段结构按 ZCode 实测口径：limits / MCP / 模型明细）。</summary>
    private static GlmState GlmSampleReady()
    {
        var state = new GlmState();
        var now = DateTimeOffset.UtcNow;
        state.Apply(GlmFetchResult.Ok(new GlmUsageData
        {
            Provider = GlmProvider.Zai,
            Level = "individual-coding-plan",
            Limits = new[]
            {
                new GlmQuotaLimit
                {
                    Type = "TOKENS_LIMIT",
                    Unit = 3,
                    Number = 5,
                    Usage = 42,
                    Remaining = 58,
                    Percentage = 42,
                    NextResetTime = now.AddHours(3).AddMinutes(12),
                },
                new GlmQuotaLimit
                {
                    Type = "TOKENS_LIMIT",
                    Unit = 6,
                    Usage = 21,
                    Remaining = 79,
                    Percentage = 21,
                    NextResetTime = now.AddDays(4).AddHours(6),
                },
                new GlmQuotaLimit
                {
                    Type = "TIME_LIMIT",
                    Unit = 5,
                    Number = 1,
                    Usage = 136,
                    Remaining = 864,
                    Percentage = 13.6,
                    NextResetTime = now.AddDays(18),
                },
            },
            McpQuota = new GlmMcpQuota
            {
                Used = 136,
                Limit = 1000,
                Remaining = 864,
                Level = "basic",
                NextRefreshAt = now.AddDays(18),
            },
            Models = new[]
            {
                new GlmModelUsage { ModelName = "GLM-5.3", CachedInputTokens = 12_480_000, UncachedInputTokens = 1_240_000, OutputTokens = 486_000, TotalTokens = 14_206_000, TotalCredits = 42.6 },
                new GlmModelUsage { ModelName = "GLM-5.3-Flash", CachedInputTokens = 8_920_000, UncachedInputTokens = 640_000, OutputTokens = 291_000, TotalTokens = 9_851_000, TotalCredits = 18.2 },
                new GlmModelUsage { ModelName = "GLM-5", CachedInputTokens = 3_180_000, UncachedInputTokens = 412_000, OutputTokens = 138_000, TotalTokens = 3_730_000, TotalCredits = 11.4 },
            },
            McpTools = new[]
            {
                new GlmMcpToolUsage { Name = "web-search", CallCount = 96, TotalCredits = 9.6 },
                new GlmMcpToolUsage { Name = "web-reader", CallCount = 40, TotalCredits = 4.0 },
            },
            TotalTokens = 27_787_000,
            TotalMcpCallCount = 136,
            RangeStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero),
            RangeEnd = now,
        }));
        return state;
    }

    private static GlmState GlmSampleNotConfigured()
    {
        var state = new GlmState();
        state.Apply(GlmFetchResult.Fail(FetchFailureKind.NotConfigured,
            "尚未设置 Z.ai（国际版）密钥，点击下方「GLM 密钥」配置。"));
        return state;
    }

    private static CodexState CodexSampleNotInstalled()
    {
        var state = new CodexState();
        CodexStatePolicy.ApplyNotInstalled(state);
        return state;
    }

    private static string SampleJson()
    {
        static string Resets(double hours)
            => DateTime.UtcNow.AddHours(hours).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

        return "{\"usage\":{\"rolling\":{\"percent\":1.2,\"status\":\"ok\",\"resetsAt\":\"" + Resets(3.75)
            + "\"},\"weekly\":{\"percent\":4,\"status\":\"ok\",\"resetsAt\":\"" + Resets(91)
            + "\"},\"monthly\":{\"percent\":2,\"status\":\"ok\",\"resetsAt\":\"" + Resets(650) + "\"}}}";
    }
}
