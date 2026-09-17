using System.Drawing;

namespace TokenDock;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Any(a => a.Equals("--selftest", StringComparison.OrdinalIgnoreCase)))
            return SelfTest.Run();

        if (args.Any(a => a.Equals("--uipreview", StringComparison.OrdinalIgnoreCase)))
        {
            ApplicationConfiguration.Initialize();
            return UiPreview.Run();
        }

        if (args.Any(a => a.Equals("--codexcheck", StringComparison.OrdinalIgnoreCase)))
            return CodexCheck.Run();

        if (args.Any(a => a.Equals("--appearance-check", StringComparison.OrdinalIgnoreCase)))
        {
            ApplicationConfiguration.Initialize();
            return SelfTest.AppearanceCheck();
        }

        if (args.Any(a => a.Equals("--tokenscheck", StringComparison.OrdinalIgnoreCase)))
            return TokenCheck.Run();

        if (args.Any(a => a.Equals("--glmcheck", StringComparison.OrdinalIgnoreCase)))
            return GlmCheck.Run();

        ApplicationConfiguration.Initialize();
        AppDataPaths.EnsureMigrated(); // 改名 TokenDock 前保存的密钥/设置自动迁移

        // macOS 风格主题按主屏 DPI 初始化（字体像素化 + 布局显式缩放，保证 100/125/150% 一致）
        using (var desktop = Graphics.FromHwnd(IntPtr.Zero))
        {
            UiTheme.Init(desktop.DpiX / 96f);
        }

        Appearance.Load(); // 读取外观设置（浅色/暗色/跟随系统 · 普通/毛玻璃）并应用调色板

        // 托盘常驻程序：UI 线程未捕获异常时提示而不退出
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
            MessageBox.Show("发生未处理的异常：" + e.Exception.Message, "错误",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                MessageBox.Show("发生严重异常，程序即将退出：" + ex.Message, "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
        };

        using var mutex = new Mutex(true, "TokenDock.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show("TokenDock · AI 用量助手已在运行，请查看系统托盘。", "提示",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }

        Application.Run(new TrayApplicationContext());
        return 0;
    }
}
