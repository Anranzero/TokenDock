using System.Drawing;
using System.Reflection;

namespace TokenDock;

/// <summary>托盘/应用图标：从嵌入的 TokenDock.ico 加载（多尺寸 ICO，系统自动选最合适的大小）。</summary>
internal static class TrayIconFactory
{
    private const string ResourceName = "TokenDock.TokenDock.ico";

    public static Icon LoadTrayIcon()
    {
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
            if (stream is not null)
                return new Icon(stream);
        }
        catch (Exception)
        {
            // 资源缺失时退回系统图标
        }

        return SystemIcons.Application;
    }
}
