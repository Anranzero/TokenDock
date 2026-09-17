using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace TokenDock;

/// <summary>
/// --tokenscheck：本机 OpenCode Go Token 采集自检——只读扫描各客户端会话库
/// （按客户端独立展示，不合并为账号总量），并与官方 `opencode stats --models`
/// 的输出逐模型交叉核对（该对照仅对 OpenCode 客户端的记录有意义）。
/// </summary>
internal static class TokenCheck
{
    public static int Run()
    {
        AttachParentConsole();
        Console.WriteLine("本机 OpenCode Go Token 采集自检（按客户端独立，不合并为账号总量）");
        Console.WriteLine("OpenCode 数据目录：" + TokenUsageReader.DataDirectory);
        Console.WriteLine("Zcode 会话库：" + TokenUsageReader.ZcodeDatabasePath);

        var report = TokenUsageReader.Scan();
        if (!report.Available)
        {
            Console.WriteLine("[失败] " + report.Detail);
            return 1;
        }

        ClientTokenUsage? opencode = null;
        foreach (var client in report.Clients)
        {
            Console.WriteLine();
            if (!client.Available)
            {
                Console.WriteLine($"[{client.ClientName}] 不可用：{client.Detail}");
                continue;
            }

            Console.WriteLine($"[通过] {client.ClientName} 客户端：{client.Source}，"
                + $"provider={TokenUsageMath.GoProviderId}* 记录 {client.Records.Count} 条（已按消息 ID 去重）");
            if (client.FailedDatabaseCount > 0)
                Console.WriteLine($"       注意：{client.FailedDatabaseCount} 个库读取失败");
            if (client.EarliestUtc is { } earliest && client.LatestUtc is { } latest)
                Console.WriteLine($"       覆盖范围：{earliest.ToLocalTime():yyyy-MM-dd HH:mm} ~ {latest.ToLocalTime():yyyy-MM-dd HH:mm}（本机记录，不代表账号总量）");

            PrintTotals(client);
            if (client.ClientName == "OpenCode")
                opencode = client;
        }

        if (opencode is null || opencode.Records.Count == 0)
        {
            Console.WriteLine();
            Console.WriteLine("[跳过] OpenCode 客户端无会话数据，未做官方对照。");
            return 0;
        }

        var opencodeClient = opencode;
        var totals = TokenUsageMath.Aggregate(opencodeClient.Records);
        var opencodeCli = FindOpencode();
        if (opencodeCli is null)
        {
            Console.WriteLine();
            Console.WriteLine("[跳过] 未找到 opencode CLI，未做官方对照（本地聚合已输出）。");
            return 0;
        }

        var cliOutput = RunOpencodeStats(opencodeCli);
        if (cliOutput is null)
        {
            Console.WriteLine();
            Console.WriteLine("[跳过] opencode stats --models 执行失败，未做官方对照。");
            return 0;
        }

        var cli = OpenCodeStatsParser.ParseGoModels(cliOutput);
        Console.WriteLine();
        Console.WriteLine($"与 opencode stats --models 对照（仅 OpenCode 客户端记录，解析到 {cli.Count} 个 opencode-go 模型）：");
        var compared = 0;
        var mismatches = 0;
        foreach (var ours in totals)
        {
            if (!cli.TryGetValue(ours.Model, out var theirs))
            {
                Console.WriteLine($"  ? {ours.Model}：官方输出中未找到该模型");
                continue;
            }

            compared++;
            var ok = theirs.Messages == ours.Messages
                && OpenCodeStatsParser.ApproximatelyEqual(theirs.Input, ours.Input)
                && OpenCodeStatsParser.ApproximatelyEqual(theirs.Output, ours.Output)
                && OpenCodeStatsParser.ApproximatelyEqual(theirs.CacheRead, ours.CacheRead)
                && OpenCodeStatsParser.ApproximatelyEqual(theirs.CacheWrite, ours.CacheWrite);
            if (!ok) mismatches++;

            Console.WriteLine($"  {(ok ? "✓" : "✗")} {ours.Model}：消息 {ours.Messages}（官方 {theirs.Messages}），"
                + $"输入 {TokenUsageMath.FormatTokens(ours.Input)}（官方 {TokenUsageMath.FormatTokens((long)Math.Round(theirs.Input))}），"
                + $"输出 {TokenUsageMath.FormatTokens(ours.Output)}（官方 {TokenUsageMath.FormatTokens((long)Math.Round(theirs.Output))}），"
                + $"缓存读 {TokenUsageMath.FormatTokens(ours.CacheRead)}（官方 {TokenUsageMath.FormatTokens((long)Math.Round(theirs.CacheRead))}）");
        }

        Console.WriteLine(mismatches == 0
            ? $"[通过] {compared} 个模型与官方统计一致。"
            : $"[失败] {mismatches} 个模型与官方统计不一致。");
        return mismatches == 0 ? 0 : 1;
    }

    private static void PrintTotals(ClientTokenUsage client)
    {
        Console.WriteLine($"本地聚合（{client.ClientName} 客户端；输出 = output + reasoning，与官方 stats 口径一致）：");
        foreach (var t in TokenUsageMath.Aggregate(client.Records))
        {
            Console.WriteLine($"  {t.Model,-18} 消息 {t.Messages,5}  输入 {TokenUsageMath.FormatTokens(t.Input),8}"
                + $"  输出 {TokenUsageMath.FormatTokens(t.Output),8}  缓存读 {TokenUsageMath.FormatTokens(t.CacheRead),8}"
                + $"  缓存写 {TokenUsageMath.FormatTokens(t.CacheWrite),7}  合计 {TokenUsageMath.FormatTokens(t.Total),8}");
        }
    }

    private static string? FindOpencode()
    {
        var names = new[] { "opencode.exe", "opencode.cmd", "opencode.bat", "opencode" };
        var directories = new List<string>();
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var part in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = part.Trim().Trim('"');
            if (trimmed.Length > 0) directories.Add(trimmed);
        }

        var npmGlobal = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm");
        if (Directory.Exists(npmGlobal)) directories.Insert(0, npmGlobal);

        foreach (var directory in directories)
        {
            foreach (var name in names)
            {
                var candidate = Path.Combine(directory, name);
                if (File.Exists(candidate)) return candidate;
            }
        }

        return null;
    }

    private static string? RunOpencodeStats(string executable)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
            };
            if (executable.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase)
                || executable.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
            {
                startInfo.FileName = "cmd.exe";
                startInfo.Arguments = $"/c \"\"{executable}\" stats --models\"";
            }
            else
            {
                startInfo.FileName = executable;
                startInfo.Arguments = "stats --models";
            }

            using var process = Process.Start(startInfo);
            if (process is null) return null;
            var output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(60000))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception)
                {
                    // 已自行退出
                }
                return null;
            }
            return output;
        }
        catch (Exception)
        {
            return null;
        }
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
            // 非 Windows 环境忽略
        }
    }
}
