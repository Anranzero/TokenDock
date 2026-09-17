using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace TokenDock;

/// <summary>
/// `--selftest` 命令行自检：无 GUI 验证响应解析、状态码分类、失败保留旧数据、
/// 倒计时格式与 DPAPI 加解密往返（内存中，不触碰真实密钥文件）。
/// </summary>
internal static class SelfTest
{
    public static int Run()
    {
        AttachParentConsole();
        var failures = 0;
        void Check(string name, bool ok)
        {
            Console.WriteLine((ok ? "[通过] " : "[失败] ") + name);
            if (!ok) failures++;
        }

        Console.WriteLine("TokenDock · AI 用量助手 自检");
        Console.WriteLine("数据源接口：" + UsageApiClient.DefaultEndpoint);

        // 1. 正常响应解析 + 剩余百分比 = 100 - 已用百分比
        var ok = UsageApiClient.ParseSuccessBody(
            "{\"usage\":{\"rolling\":{\"percent\":82.5,\"status\":\"ok\",\"resetsAt\":\"2026-09-17T12:00:00Z\"},"
            + "\"weekly\":{\"percent\":10,\"status\":\"warning\"},\"monthly\":{\"percent\":3}}}");
        Check("正常响应解析：rolling 剩余 = 100 - 82.5 = 17.5",
            ok.Success && Math.Abs((ok.Data!.Rolling!.RemainingPercent ?? -1) - 17.5) < 0.001);
        Check("正常响应解析：weekly.status 原样保留", ok.Success && ok.Data!.Weekly!.Status == "warning");
        Check("正常响应解析：resetsAt 解析为 UTC 时间",
            ok.Success
            && ok.Data!.Rolling!.ResetsAt is { } t
            && Math.Abs((t - new DateTimeOffset(2026, 9, 17, 12, 0, 0, TimeSpan.Zero)).TotalMinutes) < 1);

        // 2. 边界与容错
        var over = UsageApiClient.ParseSuccessBody("{\"usage\":{\"rolling\":{\"percent\":120}}}");
        Check("已用 120% 时剩余钳制为 0（不为负、不显示满额）",
            over.Success && over.Data!.Rolling!.RemainingPercent == 0);

        var strPercent = UsageApiClient.ParseSuccessBody("{\"usage\":{\"rolling\":{\"percent\":\"55.5\"}}}");
        Check("percent 为字符串数字时可兼容解析",
            strPercent.Success && Math.Abs((strPercent.Data!.Rolling!.RemainingPercent ?? -1) - 44.5) < 0.001);

        Check("缺少 usage 字段 → 解析失败",
            UsageApiClient.ParseSuccessBody("{\"foo\":1}").FailureKind == FetchFailureKind.ParseError);
        Check("usage 为空对象 → 解析失败（可能无订阅或格式变更）",
            UsageApiClient.ParseSuccessBody("{\"usage\":{}}").FailureKind == FetchFailureKind.ParseError);
        Check("非 JSON 响应 → 解析失败",
            UsageApiClient.ParseSuccessBody("<html>500</html>").FailureKind == FetchFailureKind.ParseError);

        // 3. 状态码分类（401 错误体为实测形态）
        var c401 = UsageApiClient.Classify(HttpStatusCode.Unauthorized,
            "{\"type\":\"error\",\"error\":{\"type\":\"AuthError\",\"message\":\"Unauthorized\"}}");
        Check("HTTP 401 → 密钥无效，并提取服务端 message",
            c401.FailureKind == FetchFailureKind.InvalidKey && c401.ServerDetail == "Unauthorized");
        Check("HTTP 403 → 无订阅 / 无权限",
            UsageApiClient.Classify(HttpStatusCode.Forbidden, "{}").FailureKind == FetchFailureKind.NoSubscription);
        Check("HTTP 429 → 限流",
            UsageApiClient.Classify(HttpStatusCode.TooManyRequests, "{}").FailureKind == FetchFailureKind.RateLimited);
        Check("HTTP 500 → 服务端异常",
            UsageApiClient.Classify(HttpStatusCode.InternalServerError, "{}").FailureKind == FetchFailureKind.ServerError);

        // 4. 失败保留旧数据并标记过期，不能显示为满额
        var state = new AppState();
        state.Apply(ok);
        state.Apply(UsageApiClient.Classify(HttpStatusCode.Unauthorized, "{}"));
        Check("失败后保留旧数据且标记过期",
            state.IsStale && state.LastGood is not null && state.LastGood.Rolling!.RemainingPercent == 17.5);
        Check("失败状态文本包含原因与最后成功时间",
            state.StatusText.Contains("401") && state.StatusText.Contains("最后成功"));

        var empty = new AppState();
        empty.Apply(FetchResult.Fail(FetchFailureKind.NetworkError, "网络连接失败或超时，请检查网络。"));
        Check("从未成功过时只显示失败原因、不显示数据",
            empty.LastGood is null && empty.StatusText.Contains("网络连接失败"));

        // 5. 倒计时格式
        var now = new DateTimeOffset(2026, 9, 17, 8, 0, 0, TimeSpan.Zero);
        Check("倒计时：缺省 → 重置时间未知", DisplayFormat.FormatCountdown(null, now) == "重置时间未知");
        Check("倒计时：90 分钟 → 1小时30分钟后重置",
            DisplayFormat.FormatCountdown(now.AddMinutes(90), now) == "1小时30分钟后重置");
        Check("倒计时：已过期 → 提示等待刷新",
            DisplayFormat.FormatCountdown(now.AddMinutes(-1), now).Contains("已到重置时间"));

        // 6. DPAPI 加解密往返（仅内存样例，不写入真实密钥文件）
        try
        {
            var plain = Encoding.UTF8.GetBytes("selftest-sample-key");
            var entropy = Encoding.UTF8.GetBytes("TokenDock.SelfTest.Entropy");
            var encrypted = ProtectedData.Protect(plain, entropy, DataProtectionScope.CurrentUser);
            var decrypted = ProtectedData.Unprotect(encrypted, entropy, DataProtectionScope.CurrentUser);
            Check("DPAPI 加解密往返", decrypted.SequenceEqual(plain));
        }
        catch (Exception ex)
        {
            Check("DPAPI 加解密往返（异常：" + ex.Message + "）", false);
        }

        // 7. 外观：调色板对比度与设置持久化
        CheckThemeContrast(Check);
        CheckAppearancePersistence(Check);

        Console.WriteLine("密钥存储位置：" + SecureKeyStore.StoreDirectory);
        Console.WriteLine("外观设置位置：" + Path.Combine(AppDataPaths.Directory, "settings.json"));
        Console.WriteLine(failures == 0 ? "全部自检通过。" : $"自检未通过 {failures} 项。");
        return failures == 0 ? 0 : 1;
    }

    /// <summary>浅色 / 暗色下调色板文字对比度（WCAG）：主文字 ≥ 7、次文字 ≥ 4（保证暗色不发灰、浅色不发白）。</summary>
    private static void CheckThemeContrast(Action<string, bool> check)
    {
        foreach (var (name, dark) in new[] { ("浅色", false), ("暗色", true) })
        {
            var p = UiTheme.PaletteFor(dark, glass: false);
            var primary = UiTheme.ContrastRatio(p.TextPrimary, p.Card);
            var secondary = UiTheme.ContrastRatio(p.TextSecondary, p.Card);
            var secondaryOnCanvas = UiTheme.ContrastRatio(p.TextSecondary, p.Canvas);
            check($"{name}主题：主文字/卡片对比度 {primary:0.00}（要求 ≥ 7）", primary >= 7.0);
            check($"{name}主题：次文字/卡片对比度 {secondary:0.00}（要求 ≥ 4）", secondary >= 4.0);
            check($"{name}主题：次文字/画布对比度 {secondaryOnCanvas:0.00}（要求 ≥ 4）", secondaryOnCanvas >= 4.0);
        }
    }

    /// <summary>外观设置存取往返（临时文件，不触碰真实设置）。</summary>
    private static void CheckAppearancePersistence(Action<string, bool> check)
    {
        var path = Path.Combine(Path.GetTempPath(), "tokendock-selftest", "settings-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            AppearanceStore.SaveTo(new AppearanceSettings
            {
                Theme = ThemeChoice.Dark,
                Effect = WindowEffectChoice.Glass,
                GlassOpacity = 0.82,
                Animations = false,
            }, path);
            var loaded = AppearanceStore.LoadFrom(path);
            check("外观设置往返：主题/效果/透明度/动画全部保留",
                loaded.Theme == ThemeChoice.Dark && loaded.Effect == WindowEffectChoice.Glass
                && Math.Abs(loaded.GlassOpacity - 0.82) < 0.001 && !loaded.Animations);
            check("透明度越界自动收敛（1.5 → 1.0，缺失 → 默认）",
                AppearanceSettings.ClampOpacity(1.5) == 1.0
                && AppearanceSettings.ClampOpacity(double.NaN) == AppearanceSettings.DefaultOpacity);
        }
        catch (Exception ex)
        {
            check("外观设置往返（异常：" + ex.Message + "）", false);
        }
        finally
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception)
            {
                // 临时文件清理失败可忽略
            }
        }
    }

    /// <summary>
    /// --appearance-check：把各外观组合应用到真实窗口句柄，验证调用不抛错并输出实际生效的模糊档位。
    /// 真实模糊是系统合成效果，只能人眼确认；此处验证「不报错 + 降级路径可达」。
    /// </summary>
    public static int AppearanceCheck()
    {
        AttachParentConsole();
        var failures = 0;
        void Check(string name, bool ok)
        {
            Console.WriteLine((ok ? "[通过] " : "[失败] ") + name);
            if (!ok) failures++;
        }

        Console.WriteLine("TokenDock 外观自检（主题 / 毛玻璃 / 降级策略）");
        Console.WriteLine("系统版本：" + WindowEffects.OsVersion + (WindowEffects.IsWindows11 ? "（Windows 11）" : "（Windows 10 或更早）"));
        Console.WriteLine("模糊能力：" + WindowEffects.DescribeCapability());
        Console.WriteLine();

        using var probe = new Form
        {
            Text = "TokenDock 外观自检",
            ClientSize = new Size(360, 200),
            ShowInTaskbar = false,
            FormBorderStyle = FormBorderStyle.FixedToolWindow,
        };
        probe.Show();
        try
        {
            foreach (var dark in new[] { false, true })
            {
                foreach (var glass in new[] { false, true })
                {
                    UiTheme.ApplyPalette(dark, glass);
                    var opacity = glass ? AppearanceSettings.DefaultOpacity : 1.0;
                    probe.Opacity = opacity;
                    var kind = WindowEffects.Apply(probe.Handle, glass, opacity, dark);
                    var label = $"主题={(dark ? "暗色" : "浅色")} · 效果={(glass ? "毛玻璃" : "普通")}";
                    Check($"{label} → 模糊档位 {kind}，窗口不透明度 {probe.Opacity:0.00}，调用无异常",
                        !glass || Math.Abs(probe.Opacity - AppearanceSettings.DefaultOpacity) < 0.001);
                }
            }
        }
        catch (Exception ex)
        {
            Check("应用外观组合（异常：" + ex.Message + "）", false);
        }
        finally
        {
            probe.Close();
            probe.Dispose();
            UiTheme.ApplyPalette(Appearance.IsDark, Appearance.IsGlass); // 还原本次会话调色板
        }

        Console.WriteLine();
        CheckThemeContrast(Check);
        Console.WriteLine(failures == 0 ? "外观自检通过。" : $"外观自检未通过 {failures} 项。");
        return failures == 0 ? 0 : 1;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int dwProcessId);

    private static void AttachParentConsole()
    {
        try
        {
            AttachConsole(-1);
        }
        catch (DllNotFoundException)
        {
            // 非 Windows 环境忽略（程序本身仅支持 Windows）
        }
    }
}
