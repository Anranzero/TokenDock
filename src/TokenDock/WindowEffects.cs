using System.Runtime.InteropServices;

namespace TokenDock;

/// <summary>当前窗口实际获得的模糊档位（供自检输出；None = 仅半透明磨砂降级）。</summary>
internal enum BlurKind
{
    /// <summary>未获得模糊（不透明白背景关闭 / 远程会话 / 系统不支持）——半透明磨砂降级。</summary>
    None,

    /// <summary>Win10 1709+：Aero BlurBehind（模糊桌面背景）。</summary>
    BlurBehind,

    /// <summary>Win11：Acrylic 亚克力（模糊 + 噪点 + 着色）。</summary>
    Acrylic
}

/// <summary>
/// 窗口效果层（纯 Win32/DWM，无第三方依赖）：
/// - 毛玻璃：先做「整窗半透明」（Form.Opacity，任何系统可用），再尝试 DWM 真模糊
///   （Win11 用 Acrylic、Win10 用 BlurBehind；远程会话/调用失败自动跳过）。
///   两者叠加即「半透明 + 背景高斯模糊」；模糊不可用时保留半透明磨砂，不报错。
/// - 深色标题栏：DWMWA_USE_IMMERSIVE_DARK_MODE（Win10 1809+ 属性 19 / 18985+ 属性 20）。
/// </summary>
internal static class WindowEffects
{
    private const int WcaAccentPolicy = 19;
    private const int AccentEnableBlurBehind = 3;
    private const int AccentEnableAcrylicBlurBehind = 4;
    private const int AccentDisabled = 0;
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeLegacy = 19;
    private const int SmRemoteSession = 0x1000;

    /// <summary>最近一次应用到窗口的模糊档位（诊断用）。</summary>
    public static BlurKind LastApplied { get; private set; } = BlurKind.None;

    /// <summary>系统版本号（如 10.0.22621）；取不到时为 0。</summary>
    public static Version OsVersion { get; } = Environment.OSVersion.Version;

    /// <summary>Windows 11（build ≥ 22000）。</summary>
    public static bool IsWindows11 => OsVersion.Major >= 10 && OsVersion.Build >= 22000;

    /// <summary>支持 SetWindowCompositionAttribute 的系统（Win10 1709+ / build ≥ 16299）。</summary>
    public static bool SupportsCompositionAttribute =>
        OsVersion.Major >= 10 && OsVersion.Build >= 16299 && !IsRemoteSession();

    /// <summary>远程桌面会话中 Acrylic/模糊会渲染为不透明灰块，直接跳过模糊层。</summary>
    public static bool IsRemoteSession()
    {
        try
        {
            return GetSystemMetrics(SmRemoteSession) != 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// 应用窗口效果。glass=false 时恢复不透明并关闭模糊。
    /// 任何一步失败都静默降级（最差为半透明磨砂），绝不抛出。
    /// </summary>
    public static BlurKind Apply(IntPtr handle, bool glass, double opacity, bool dark)
    {
        if (handle == IntPtr.Zero) return BlurKind.None;

        ApplyDarkTitleBar(handle, dark);

        if (!glass)
        {
            try { Accent(handle, AccentDisabled, 0); } catch (Exception) { /* 忽略：可能系统不支持 */ }
            return LastApplied = BlurKind.None;
        }

        var kind = BlurKind.None;
        if (SupportsCompositionAttribute)
        {
            // Win11 用 Acrylic（带噪点着色，更高级）；Win10 用 BlurBehind（拖拽更跟手、无 RDP 灰块问题）
            var state = IsWindows11 ? AccentEnableAcrylicBlurBehind : AccentEnableBlurBehind;
            // GradientColor 的 alpha 控制着色强度（0x01 起才有模糊；透亮度由整窗 Opacity 负责）
            var tint = 0x01000000; // AABBGGRR：极低 alpha 的黑，仅唤醒模糊不额外压暗
            if (Accent(handle, state, tint))
                kind = IsWindows11 ? BlurKind.Acrylic : BlurKind.BlurBehind;
        }

        LastApplied = kind;
        return kind;
    }

    private static void ApplyDarkTitleBar(IntPtr handle, bool dark)
    {
        var value = dark ? 1 : 0;
        try
        {
            if (DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref value, sizeof(int)) != 0)
                DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkModeLegacy, ref value, sizeof(int));
        }
        catch (Exception)
        {
            // 旧系统 / 无 DWM：标题栏保持系统默认
        }
    }

    /// <summary>SetWindowCompositionAttribute 应用/关闭亚克力着色；失败返回 false。</summary>
    private static bool Accent(IntPtr handle, int state, int gradientColor)
    {
        try
        {
            var policy = new AccentPolicy
            {
                AccentState = state,
                AccentFlags = 0,
                GradientColor = gradientColor,
                AnimationId = 0,
            };
            var size = Marshal.SizeOf<AccentPolicy>();
            var ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(policy, ptr, false);
                var data = new WindowCompositionAttributeData
                {
                    Attribute = WcaAccentPolicy,
                    Data = ptr,
                    SizeOfData = size,
                };
                return SetWindowCompositionAttribute(handle, ref data) != 0;
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }
        catch (Exception)
        {
            return false; // 入口点缺失 / 系统不支持
        }
    }

    /// <summary>界面预览 / 自检用：描述当前环境可用的模糊档位。</summary>
    public static string DescribeCapability()
    {
        if (IsRemoteSession())
            return "远程会话：跳过模糊，仅整窗半透明（磨砂降级）";
        if (!SupportsCompositionAttribute)
            return $"系统版本 {OsVersion}：不支持合成属性，仅整窗半透明（磨砂降级）";
        return IsWindows11
            ? $"Windows 11（{OsVersion}）：尝试 Acrylic 亚克力模糊"
            : $"Windows 10（{OsVersion}）：尝试 Aero BlurBehind 模糊";
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public int GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
}
