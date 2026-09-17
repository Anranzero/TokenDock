using Xunit;

namespace TokenDock.Tests;

/// <summary>外观设置：透明度收敛、存取往返、旧设置兼容、调色板对比度（含毛玻璃边框独立于主题）。</summary>
public class AppearanceTests
{
    [Theory]
    [InlineData(0.3, 0.6)]
    [InlineData(0.6, 0.6)]
    [InlineData(0.88, 0.88)]
    [InlineData(1.0, 1.0)]
    [InlineData(1.5, 1.0)]
    [InlineData(double.NaN, AppearanceSettings.DefaultOpacity)]
    public void ClampOpacity_ConvergesIntoRange(double input, double expected)
        => Assert.Equal(expected, AppearanceSettings.ClampOpacity(input), 3);

    [Fact]
    public void Store_RoundTripKeepsAllFields()
    {
        var path = Path.Combine(Path.GetTempPath(), "opencode-tests", "appearance-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var settings = new AppearanceSettings
            {
                Theme = ThemeChoice.Dark,
                Effect = WindowEffectChoice.Glass,
                GlassOpacity = 0.76,
                Animations = false,
            };
            AppearanceStore.SaveTo(settings, path);
            var loaded = AppearanceStore.LoadFrom(path);

            Assert.Equal(ThemeChoice.Dark, loaded.Theme);
            Assert.Equal(WindowEffectChoice.Glass, loaded.Effect);
            Assert.Equal(0.76, loaded.GlassOpacity, 3);
            Assert.False(loaded.Animations);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Store_IgnoresLegacyRateFields_AndDefaultsMissing()
    {
        var path = Path.Combine(Path.GetTempPath(), "opencode-tests", "appearance-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            // 旧版 settings.json（含已移除的汇率字段）应可解析并回落默认外观
            File.WriteAllText(path, "{\"RateMode\":\"manual\",\"ManualRate\":7.2,\"CachedRate\":6.7071}");
            var loaded = AppearanceStore.LoadFrom(path);
            Assert.Equal(ThemeChoice.System, loaded.Theme);
            Assert.Equal(WindowEffectChoice.Normal, loaded.Effect);
            Assert.True(loaded.Animations);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Store_MissingOrBrokenFile_FallsBackToDefaults()
    {
        var missing = AppearanceStore.LoadFrom(Path.Combine(Path.GetTempPath(), "opencode-tests", "nope-" + Guid.NewGuid().ToString("N") + ".json"));
        Assert.Equal(ThemeChoice.System, missing.Theme);
        Assert.Equal(AppearanceSettings.DefaultOpacity, missing.GlassOpacity, 3);

        var brokenPath = Path.Combine(Path.GetTempPath(), "opencode-tests", "broken-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(brokenPath, "{ not json");
            var broken = AppearanceStore.LoadFrom(brokenPath);
            Assert.Equal(ThemeChoice.System, broken.Theme);
        }
        finally
        {
            if (File.Exists(brokenPath)) File.Delete(brokenPath);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Palette_TextContrastIsReadable(bool dark)
    {
        var p = UiTheme.PaletteFor(dark, glass: false);
        Assert.True(UiTheme.ContrastRatio(p.TextPrimary, p.Card) >= 7.0, "主文字/卡片对比度不足");
        Assert.True(UiTheme.ContrastRatio(p.TextSecondary, p.Card) >= 4.0, "次文字/卡片对比度不足");
        Assert.True(UiTheme.ContrastRatio(p.TextSecondary, p.Canvas) >= 4.0, "次文字/画布对比度不足");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GlassPalette_OnlyDiffsBorders(bool dark)
    {
        // 毛玻璃只调整边框（更细更淡），文字与底色必须与普通模式一致，避免出现主题外的新配色
        var normal = UiTheme.PaletteFor(dark, glass: false);
        var glass = UiTheme.PaletteFor(dark, glass: true);
        Assert.Equal(normal.TextPrimary, glass.TextPrimary);
        Assert.Equal(normal.TextSecondary, glass.TextSecondary);
        Assert.Equal(normal.Card, glass.Card);
        Assert.Equal(normal.Canvas, glass.Canvas);
        Assert.Equal(normal.Green, glass.Green);
        Assert.NotEqual(normal.CardBorder, glass.CardBorder);
        Assert.True(glass.CardBorder.A < 255, "毛玻璃边框应为半透明（更细更淡）");
    }

    [Fact]
    public void EnumOrder_MatchesSettingsPageIndexMapping()
    {
        // 设置页用 (int) 下标映射单选段控件，顺序变化会静默错位——在这里锁死
        Assert.Equal(0, (int)ThemeChoice.Light);
        Assert.Equal(1, (int)ThemeChoice.Dark);
        Assert.Equal(2, (int)ThemeChoice.System);
        Assert.Equal(0, (int)WindowEffectChoice.Normal);
        Assert.Equal(1, (int)WindowEffectChoice.Glass);
    }

    [Fact]
    public void WindowEffects_CapabilityProbeNeverThrows()
    {
        var text = WindowEffects.DescribeCapability();
        Assert.False(string.IsNullOrWhiteSpace(text));
        _ = WindowEffects.IsWindows11;
        _ = WindowEffects.SupportsCompositionAttribute;
    }
}
