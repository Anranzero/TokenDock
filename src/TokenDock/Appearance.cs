using System.Text.Json;
using System.Windows.Forms;

namespace TokenDock;

/// <summary>主题选择：浅色 / 暗色 / 跟随系统。</summary>
internal enum ThemeChoice
{
    Light,
    Dark,
    System
}

/// <summary>窗口效果：普通 / 毛玻璃。</summary>
internal enum WindowEffectChoice
{
    Normal,
    Glass
}

/// <summary>外观设置（可持久化）。</summary>
internal sealed class AppearanceSettings
{
    public ThemeChoice Theme { get; set; } = ThemeChoice.System;
    public WindowEffectChoice Effect { get; set; } = WindowEffectChoice.Normal;

    /// <summary>毛玻璃透明度（0.5 ~ 1.0；1.0 表示最不透明）。</summary>
    public double GlassOpacity { get; set; } = DefaultOpacity;

    /// <summary>进度条缓动等界面动画。</summary>
    public bool Animations { get; set; } = true;

    public const double DefaultOpacity = 0.88;

    /// <summary>下限 0.6：低于此值文字对比度不可靠（要求「文字保持清晰可读」）。</summary>
    public const double MinOpacity = 0.6;

    public const double MaxOpacity = 1.0;

    public static double ClampOpacity(double value)
        => double.IsNaN(value) ? DefaultOpacity : Math.Clamp(value, MinOpacity, MaxOpacity);

    public AppearanceSettings Clone() => new()
    {
        Theme = Theme,
        Effect = Effect,
        GlassOpacity = GlassOpacity,
        Animations = Animations,
    };
}

/// <summary>
/// 外观设置存储（%APPDATA%\TokenDock\settings.json，与密钥分开；非敏感）。
/// 读取失败 / 文件缺失 / 残留旧字段时按默认值运行，绝不抛出。
/// </summary>
internal static class AppearanceStore
{
    private static string StoreFilePath => Path.Combine(AppDataPaths.Directory, "settings.json");

    public static AppearanceSettings Load()
    {
        AppDataPaths.EnsureMigrated();
        return LoadFrom(StoreFilePath);
    }

    public static void Save(AppearanceSettings settings)
    {
        try
        {
            SaveTo(settings, StoreFilePath);
        }
        catch (Exception)
        {
            // 落盘失败不影响本次运行（下次改外观会再试）
        }
    }

    /// <summary>写入指定文件（测试用）。</summary>
    public static AppearanceSettings LoadFrom(string path)
    {
        try
        {
            if (!File.Exists(path)) return new AppearanceSettings();
            var loaded = JsonSerializer.Deserialize<AppearanceSettings>(File.ReadAllText(path));
            if (loaded is null) return new AppearanceSettings();
            loaded.GlassOpacity = AppearanceSettings.ClampOpacity(loaded.GlassOpacity);
            return loaded;
        }
        catch (Exception)
        {
            return new AppearanceSettings();
        }
    }

    /// <summary>写入指定文件（测试用）。</summary>
    public static void SaveTo(AppearanceSettings settings, string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllText(path,
            JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
    }
}

/// <summary>
/// 外观控制器：持有当前设置 → 换算「是否暗色」（跟随系统时读注册表）→
/// 应用到调色板（UiTheme）与所有已附加窗口（背景色 / 半透明 / 真模糊 / 深色标题栏）。
/// 设置页改动即时生效并落盘；重启后由 Load() 恢复。
/// </summary>
internal static class Appearance
{
    private static readonly List<Form> AttachedForms = new();

    /// <summary>外观变化（主题 / 效果 / 透明度 / 动画任一改变）。控件据此重绘。</summary>
    public static event Action? Changed;

    public static AppearanceSettings Current { get; private set; } = new();

    /// <summary>当前生效的暗色状态（跟随系统时已解析）。</summary>
    public static bool IsDark { get; private set; }

    public static bool IsGlass => Current.Effect == WindowEffectChoice.Glass;

    /// <summary>程序启动时调用：读设置 → 应用调色板。任何异常都不抛出。</summary>
    public static void Load()
    {
        Current = AppearanceStore.Load();
        Apply(persist: false);
    }

    /// <summary>设置页调用：修改设置 → 即时应用 + 落盘。</summary>
    public static void Update(Action<AppearanceSettings> mutate)
    {
        mutate(Current);
        Current.GlassOpacity = AppearanceSettings.ClampOpacity(Current.GlassOpacity);
        Apply(persist: true);
    }

    /// <summary>定时器调用：跟随系统模式下感知系统主题切换（其他模式为无操作）。</summary>
    public static void RefreshSystemTheme()
    {
        if (Current.Theme != ThemeChoice.System) return;
        var dark = SystemPrefersDark();
        if (dark == IsDark) return;
        Apply(persist: false);
    }

    /// <summary>把窗口纳入外观管理（构造时调用）：设置背景色并应用窗口效果。</summary>
    public static void Attach(Form form)
    {
        if (!AttachedForms.Contains(form))
            AttachedForms.Add(form);
        void OnDisposed(object? sender, EventArgs e)
        {
            AttachedForms.Remove(form);
            Changed -= OnAppearanceChanged;
        }

        form.Disposed += OnDisposed;
        Changed += OnAppearanceChanged;
        UiTheme.BindBack(form, () => UiTheme.Canvas);
        ApplyToForm(form);
    }

    private static void OnAppearanceChanged()
    {
        foreach (var form in AttachedForms.ToArray())
        {
            if (form.IsDisposed) continue;
            ApplyToForm(form);
        }
    }

    private static void ApplyToForm(Form form)
    {
        try
        {
            if (!form.IsHandleCreated) return;
            form.Opacity = IsGlass ? Current.GlassOpacity : 1.0;
            WindowEffects.Apply(form.Handle, IsGlass, Current.GlassOpacity, IsDark);
        }
        catch (Exception)
        {
            // 窗口效果失败时保持不透明，不影响功能
        }
    }

    private static void Apply(bool persist)
    {
        IsDark = Current.Theme switch
        {
            ThemeChoice.Dark => true,
            ThemeChoice.Light => false,
            _ => SystemPrefersDark(),
        };

        UiTheme.ApplyPalette(IsDark, IsGlass);
        UiTheme.Animations = Current.Animations;
        if (persist)
            AppearanceStore.Save(Current);
        Changed?.Invoke();
    }

    /// <summary>读取系统「应用模式」（AppsUseLightTheme：1=浅色，0=暗色）；失败按浅色。</summary>
    private static bool SystemPrefersDark()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("AppsUseLightTheme") is int value)
                return value == 0;
        }
        catch (Exception)
        {
            // 非 Windows / 注册表不可读：按浅色
        }

        return false;
    }
}

/// <summary>托盘右键菜单主题化：深色 / 浅色随外观设置切换（自绘渲染器，颜色实时取自调色板）。</summary>
internal static class TrayMenuTheme
{
    public static void Apply(ContextMenuStrip menu)
    {
        menu.Renderer = new ThemedMenuRenderer();
        ApplyColors(menu);
    }

    /// <summary>主题变化后重新取色并强制重绘。</summary>
    public static void Refresh(ContextMenuStrip menu)
    {
        ApplyColors(menu);
        menu.Refresh();
    }

    private static void ApplyColors(ContextMenuStrip menu)
    {
        menu.BackColor = UiTheme.Card;
        menu.ForeColor = UiTheme.TextPrimary;
        foreach (ToolStripItem item in menu.Items)
            item.ForeColor = UiTheme.TextPrimary;
    }
}

/// <summary>菜单渲染器：圆角关闭、颜色全部来自当前调色板。</summary>
internal sealed class ThemedMenuRenderer : ToolStripProfessionalRenderer
{
    public ThemedMenuRenderer() : base(new ThemedMenuColorTable())
    {
        RoundedEdges = false;
    }
}

/// <summary>菜单配色表（读取时即当前主题值）。</summary>
internal sealed class ThemedMenuColorTable : ProfessionalColorTable
{
    public override Color ToolStripDropDownBackground => UiTheme.Card;
    public override Color MenuBorder => UiTheme.CardBorder;
    public override Color MenuItemBorder => UiTheme.SegmentTrack;
    public override Color MenuItemSelected => UiTheme.SecondaryBtnHover;
    public override Color MenuItemSelectedGradientBegin => UiTheme.SecondaryBtnHover;
    public override Color MenuItemSelectedGradientEnd => UiTheme.SecondaryBtnHover;
    public override Color MenuItemPressedGradientBegin => UiTheme.SecondaryBtnPress;
    public override Color MenuItemPressedGradientEnd => UiTheme.SecondaryBtnPress;
    public override Color ImageMarginGradientBegin => UiTheme.Card;
    public override Color ImageMarginGradientMiddle => UiTheme.Card;
    public override Color ImageMarginGradientEnd => UiTheme.Card;
    public override Color SeparatorDark => UiTheme.Hairline;
    public override Color SeparatorLight => UiTheme.Hairline;
}
