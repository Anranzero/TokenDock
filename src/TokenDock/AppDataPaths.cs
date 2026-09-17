namespace TokenDock;

/// <summary>
/// 应用数据目录（%APPDATA%\TokenDock）。首次访问时自动把旧目录 OpenCodeGoAssistant
/// 迁移过来，使改名前的密钥与设置平滑延续；迁移失败则按全新状态运行（用户重新设置即可）。
/// </summary>
public static class AppDataPaths
{
    private const string LegacyDirectoryName = "OpenCodeGoAssistant";

    private static bool _migrated;

    public static string Directory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TokenDock");

    /// <summary>幂等；任何异常都不抛出（最坏退化为无历史数据）。</summary>
    public static void EnsureMigrated()
    {
        if (_migrated) return;
        _migrated = true;

        try
        {
            if (System.IO.Directory.Exists(Directory)) return;
            var legacy = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), LegacyDirectoryName);
            if (!System.IO.Directory.Exists(legacy)) return;

            try
            {
                System.IO.Directory.Move(legacy, Directory);
            }
            catch (IOException)
            {
                // 目录被占用等原因无法整体移动：逐个复制文件（保留原目录，不删除）
                System.IO.Directory.CreateDirectory(Directory);
                foreach (var file in System.IO.Directory.GetFiles(legacy))
                {
                    var target = Path.Combine(Directory, Path.GetFileName(file));
                    if (!File.Exists(target)) File.Copy(file, target);
                }
            }
        }
        catch (Exception)
        {
            // 迁移失败：按全新状态运行
        }
    }
}
