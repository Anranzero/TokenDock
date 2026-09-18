using Xunit;

namespace TokenDock.Tests;

/// <summary>悬浮窗模型组装测试：三个订阅的行/倒计时、异常态提示、设置持久化与透明度收敛。</summary>
public class FloatingOverlayTests
{
    private const string OpenCodeJson = """
    {"usage":{"rolling":{"percent":40,"resetsAt":"2026-09-17T23:28:00Z"},
    "weekly":{"percent":10,"resetsAt":"2026-09-21T00:00:00Z"},"monthly":{"percent":5}}}
    """;

    private static GlmState GlmStateWithData()
    {
        var state = new GlmState();
        state.Apply(GlmFetchResult.Ok(new GlmUsageData
        {
            Provider = GlmProvider.BigModel,
            Level = "lite",
            Limits = new[]
            {
                new GlmQuotaLimit { Type = "CREDIT_LIMIT", Unit = 3, Number = 5, Usage = 2000, CurrentValue = 814, Remaining = 1185, Percentage = 40, NextResetTime = DateTimeOffset.UtcNow.AddHours(3) },
                new GlmQuotaLimit { Type = "CREDIT_LIMIT", Unit = 6, Number = 1, Usage = 10000, CurrentValue = 4824, Remaining = 5175, Percentage = 48, NextResetTime = DateTimeOffset.UtcNow.AddDays(4) },
            },
            TotalTokens = 742_540_978,
            TotalMcpCallCount = 12,
        }));
        return state;
    }

    [Fact]
    public void Compose_OpenCode_ShowsFiveHourAndWeekly()
    {
        var state = new AppState();
        state.Apply(UsageApiClient.ParseSuccessBody(OpenCodeJson));
        var model = FloatingOverlayModel.Compose(OverlaySubscription.OpenCodeGo, state, null, null);

        Assert.True(model.HasData);
        Assert.Equal("OpenCode Go", model.Title);
        Assert.Equal(2, model.Rows.Count);
        Assert.Equal("5小时剩余", model.Rows[0].Label);
        Assert.Equal(60, model.Rows[0].Percent!.Value, 1);
        Assert.Equal("每周剩余", model.Rows[1].Label);
        Assert.Equal(90, model.Rows[1].Percent!.Value, 1);
        // 最近重置 = 5 小时窗口（早于每周）
        Assert.NotNull(model.Countdown);
    }

    [Fact]
    public void Compose_OpenCode_WithoutData_ShowsHintNotZero()
    {
        var state = new AppState();
        state.Apply(FetchResult.Fail(FetchFailureKind.NotConfigured, "尚未设置 API 密钥"));
        var model = FloatingOverlayModel.Compose(OverlaySubscription.OpenCodeGo, state, null, null);

        Assert.False(model.HasData);
        Assert.Empty(model.Rows);
        Assert.Contains("尚未设置", model.Hint);
    }

    [Fact]
    public void Compose_Codex_PicksPrimaryAndWeeklyWindows()
    {
        var state = new CodexState();
        var snapshot = new CodexRateLimitsSnapshot
        {
            Groups = new[]
            {
                new CodexRateGroup
                {
                    LimitId = "codex",
                    Primary = new CodexRateWindow { UsedPercent = 30, WindowDurationMins = 300, ResetsAtUnixSeconds = 1789658904 },
                },
                new CodexRateGroup
                {
                    LimitId = "codex_bengalfox",
                    LimitName = "GPT-5.3-Codex-Spark",
                    Primary = new CodexRateWindow { UsedPercent = 10, WindowDurationMins = 10080, ResetsAtUnixSeconds = 1790183449 },
                },
            },
        };
        CodexStatePolicy.ApplySnapshot(state, new CodexAccountInfo { LoggedIn = true, Email = "user@example.com" }, snapshot);

        var model = FloatingOverlayModel.Compose(OverlaySubscription.Codex, null, state, null);
        Assert.True(model.HasData);
        Assert.Equal(2, model.Rows.Count);
        Assert.Contains("5小时", model.Rows[0].Label);
        Assert.Equal(70, model.Rows[0].Percent!.Value, 1);
        Assert.Contains("7天", model.Rows[1].Label);
        // 展开信息：2 个窗口行 + 账号行 + 更新时间
        Assert.Equal(2, model.Details.Count(d => d.Contains("：") && d.Contains("%")));
        Assert.Contains(model.Details, d => d.Contains("user@example.com"));
    }

    [Fact]
    public void Compose_Codex_NotLoggedIn_ShowsHint()
    {
        var state = new CodexState();
        CodexStatePolicy.ApplyNotInstalled(state);
        var model = FloatingOverlayModel.Compose(OverlaySubscription.Codex, null, state, null);

        Assert.False(model.HasData);
        Assert.Contains("Codex", model.Hint);
    }

    [Fact]
    public void Compose_Glm_ShowsMappedLimits_TotalTokens_AndMcpCalls()
    {
        var state = GlmStateWithData();
        var model = FloatingOverlayModel.Compose(OverlaySubscription.Glm, null, null, state);

        Assert.True(model.HasData);
        Assert.Equal(2, model.Rows.Count);
        Assert.Equal("5小时剩余", model.Rows[0].Label);
        Assert.Equal(59.25, model.Rows[0].Percent!.Value, 1); // 1185 / 2000
        Assert.Equal("每周剩余", model.Rows[1].Label);
        Assert.Equal(51.75, model.Rows[1].Percent!.Value, 2); // 5175 / 10000
        Assert.Contains("总 Token：742.5M", model.Details);
        Assert.Contains(model.Details, d => d.Contains("MCP 调用：12 次"));
    }

    [Fact]
    public void Compose_Glm_WithoutData_ShowsHintNotZero()
    {
        var state = new GlmState();
        state.Apply(GlmFetchResult.Fail(FetchFailureKind.NotConfigured, "尚未设置密钥"));
        var model = FloatingOverlayModel.Compose(OverlaySubscription.Glm, null, null, state);

        Assert.False(model.HasData);
        Assert.Null(model.Countdown);
        Assert.Contains("尚未配置", model.Hint);
    }

    [Fact]
    public void Settings_RoundTripKeepsAllFields()
    {
        var path = Path.Combine(Path.GetTempPath(), "opencode-tests", "floating-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var settings = new FloatingOverlaySettings
            {
                Subscription = OverlaySubscription.Glm,
                Enabled = true,
                Locked = true,
                Opacity = 0.75,
                X = 1920,
                Y = 1040,
            };
            FloatingOverlayStore.SaveTo(settings, path);
            var loaded = FloatingOverlayStore.LoadFrom(path);

            Assert.Equal(OverlaySubscription.Glm, loaded.Subscription);
            Assert.True(loaded.Enabled);
            Assert.True(loaded.Locked);
            Assert.Equal(0.75, loaded.Opacity, 3);
            Assert.Equal(1920, loaded.X);
            Assert.Equal(1040, loaded.Y);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Settings_MissingOrBroken_FallsBackToDefaults()
    {
        var missing = FloatingOverlayStore.LoadFrom(Path.Combine(Path.GetTempPath(), "opencode-tests", "nope.json"));
        Assert.Equal(OverlaySubscription.OpenCodeGo, missing.Subscription);
        Assert.False(missing.Enabled);
        Assert.Equal(0.92, missing.Opacity, 3);

        var broken = FloatingOverlayStore.LoadFrom("Z:\\不存在的目录\\floating.json");
        Assert.Equal(OverlaySubscription.OpenCodeGo, broken.Subscription);
    }

    [Theory]
    [InlineData(0.3, 0.6)]
    [InlineData(0.92, 0.92)]
    [InlineData(1.5, 1.0)]
    [InlineData(double.NaN, 0.92)]
    public void ClampOpacity_ConvergesIntoRange(double input, double expected)
        => Assert.Equal(expected, FloatingOverlaySettings.ClampOpacity(input), 3);

    // ---- 窗体级回归：切换订阅必须换数据（用户实测反馈"不管选什么都是 OpenCode Go"） ----

    private static (AppState Op, CodexState Codex, GlmState Glm) SampleStates()
    {
        var op = new AppState();
        op.Apply(UsageApiClient.ParseSuccessBody(OpenCodeJson));

        var codex = new CodexState();
        CodexStatePolicy.ApplySnapshot(codex, new CodexAccountInfo { LoggedIn = true },
            new CodexRateLimitsSnapshot
            {
                Groups = new[]
                {
                    new CodexRateGroup
                    {
                        LimitId = "codex",
                        Primary = new CodexRateWindow { UsedPercent = 30, WindowDurationMins = 300, ResetsAtUnixSeconds = 1789658904 },
                    },
                },
            });

        return (op, codex, GlmStateWithData());
    }

    [Fact]
    public void Form_SwitchingSubscription_ChangesModel()
    {
        UiTheme.Init(1f);
        var (op, codex, glm) = SampleStates();
        using var form = new FloatingOverlayForm(new FloatingOverlaySettings { Subscription = OverlaySubscription.OpenCodeGo, Opacity = 1.0 });
        form.SetStates(op, codex, glm);

        Assert.Equal("OpenCode Go", form.CurrentModel.Title);
        Assert.Equal("5小时剩余", form.CurrentModel.Rows[0].Label);

        form.Subscription = OverlaySubscription.Glm;
        Assert.Equal("GLM", form.CurrentModel.Title);
        Assert.Equal(59.25, form.CurrentModel.Rows[0].Percent!.Value, 1);

        form.Subscription = OverlaySubscription.Codex;
        Assert.Equal("Codex", form.CurrentModel.Title);
        Assert.Contains("5小时", form.CurrentModel.Rows[0].Label);
    }

    [Fact]
    public void Form_MenuClick_SwitchesSubscription()
    {
        UiTheme.Init(1f);
        var (op, codex, glm) = SampleStates();
        using var form = new FloatingOverlayForm(new FloatingOverlaySettings { Opacity = 1.0 });
        form.SetStates(op, codex, glm);

        // 模拟真实右键菜单点击（下标 2 = GLM）
        form.SubscriptionMenuItemForTest(2).PerformClick();
        Assert.Equal("GLM", form.CurrentModel.Title);

        form.SubscriptionMenuItemForTest(1).PerformClick();
        Assert.Equal("Codex", form.CurrentModel.Title);

        form.SubscriptionMenuItemForTest(0).PerformClick();
        Assert.Equal("OpenCode Go", form.CurrentModel.Title);
    }

    [Fact]
    public void Form_CtorRestoresSavedSubscription()
    {
        UiTheme.Init(1f);
        var (op, codex, glm) = SampleStates();
        using var form = new FloatingOverlayForm(new FloatingOverlaySettings { Subscription = OverlaySubscription.Glm, Opacity = 1.0 });
        form.SetStates(op, codex, glm);
        Assert.Equal("GLM", form.CurrentModel.Title);
    }

    // ---- 自审修复项的回归测试：多显示器 / 透明度归属 / 折行高度 ----

    [Fact]
    public void Placement_KeepsSavedPositionOnSecondaryMonitor()
    {
        var primary = new Rectangle(0, 0, 1920, 1040);
        var secondary = new Rectangle(1920, 0, 2560, 1400);
        Rectangle AreaOf(Point p) => p.X >= secondary.Left ? secondary : primary;

        var location = OverlayPlacement.ResolveInitialPlacement(2500, 300, new Size(204, 96), primary, AreaOf, 24);
        Assert.Equal(2500, location.X); // 保持在副屏，不被拉回主屏
        Assert.Equal(300, location.Y);
    }

    [Fact]
    public void Placement_FallsBackToPrimary_WhenSavedPointHasNoScreen()
    {
        var primary = new Rectangle(0, 0, 1920, 1040);
        // Screen.FromPoint 返回"最近的屏幕"；副屏被拔掉时，该点已不落在任何屏幕内
        Rectangle AreaOf(Point p) => primary;

        var location = OverlayPlacement.ResolveInitialPlacement(2500, 300, new Size(204, 96), primary, AreaOf, 24);
        Assert.True(primary.Contains(location), "显示器移除后应收敛回主屏");
    }

    [Fact]
    public void Placement_Default_IsPrimaryBottomRightWithMargin()
    {
        var primary = new Rectangle(0, 0, 1920, 1040);
        var location = OverlayPlacement.ResolveInitialPlacement(-1, -1, new Size(204, 96), primary, _ => primary, 24);
        Assert.Equal(1920 - 204 - 24, location.X);
        Assert.Equal(1040 - 96 - 24, location.Y);
    }

    [Fact]
    public void Wrap_SplitsByMeasuredWidth()
    {
        var lines = OverlayTextLayout.Wrap("ABCDEFGHIJ", 30, s => s.Length * 10);
        Assert.Equal(new[] { "ABC", "DEF", "GHI", "J" }, lines);
        Assert.Empty(OverlayTextLayout.Wrap("", 30, s => s.Length * 10));
    }

    [Fact]
    public void AppearanceChange_DoesNotClobberOverlayOpacity()
    {
        UiTheme.Init(1f);
        var original = Appearance.Current.Theme;
        try
        {
            using var form = new FloatingOverlayForm(new FloatingOverlaySettings { Opacity = 0.75 });
            _ = form.Handle; // 建立句柄，让外观应用路径真正执行
            Assert.Equal(0.75, form.Opacity, 2);

            Appearance.ApplyForTest(s => s.Theme = original == ThemeChoice.Dark ? ThemeChoice.Light : ThemeChoice.Dark);
            Assert.Equal(0.75, form.Opacity, 2); // 外观变化不得覆盖悬浮窗自身透明度
        }
        finally
        {
            Appearance.ApplyForTest(s => s.Theme = original);
        }
    }

    [Fact]
    public void ExpandedSize_CompensatesWrappedLines()
    {
        UiTheme.Init(1f);
        // 构造一条必然折行的明细（未知类型 → 原样显示长 type 文本）
        var state = new GlmState();
        state.Apply(GlmFetchResult.Ok(new GlmUsageData
        {
            Provider = GlmProvider.BigModel,
            Limits = new[]
            {
                new GlmQuotaLimit
                {
                    Type = "EXTREMELY_LONG_LIMIT_TYPE_NAME_FOR_WRAPPING_TEST",
                    Unit = 9, Usage = 1000, Remaining = 864, Percentage = 13.6,
                    NextResetTime = DateTimeOffset.UtcNow.AddDays(3),
                },
            },
            TotalTokens = 1_234_567,
        }));

        using var form = new FloatingOverlayForm(new FloatingOverlaySettings { Subscription = OverlaySubscription.Glm, Opacity = 1.0 });
        form.SetStates(null, null, state);
        form.PreviewSetExpanded(true);

        var model = form.CurrentModel;
        var singleLineAssumption = UiTheme.Px(30)
            + (model.Rows.Count + model.Details.Count) * UiTheme.Px(16)
            + (model.Countdown is null ? 0 : UiTheme.Px(14)) + UiTheme.Px(8);
        Assert.True(form.CurrentSizeForTest.Height > singleLineAssumption,
            "展开高度未按折行补偿（长行会被裁切）");
    }
}
