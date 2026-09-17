using System.Runtime.InteropServices;
using System.Text.Json;

namespace TokenDock;

/// <summary>
/// --glmcheck：GLM Coding Plan 远端账号统计自检——读取本机已保存的 Provider 密钥，
/// 调用真实远端接口并打印解析后的每一项字段（含原始响应片段），供与 ZCode
///「使用统计 → 编程套餐」逐项核对。全程不读取本机会话数据库、不回显完整密钥。
/// </summary>
internal static class GlmCheck
{
    public static int Run()
    {
        AttachParentConsole();
        Console.WriteLine("GLM Coding Plan 远端账号统计自检（只发远端请求，不读取本机数据库）");

        var provider = GlmSettingsStore.LoadProvider();
        Console.WriteLine("Provider：" + GlmEndpoints.DisplayName(provider));
        Console.WriteLine("Base URL：" + GlmEndpoints.BaseUrl(provider));

        var key = SecureKeyStore.LoadGlm(provider);
        if (key is null)
        {
            Console.WriteLine("[跳过] 未配置该 Provider 的密钥：请先在 GLM 页点「GLM 密钥」保存并验证。");
            return 0;
        }

        Console.WriteLine($"密钥：已配置（长度 {key.Length}，不回显）");
        Console.WriteLine();

        using var client = new GlmUsageClient();
        var result = client.FetchAsync(provider, key).GetAwaiter().GetResult();
        if (!result.Success)
        {
            Console.WriteLine("[失败] " + result.ErrorMessage);
            if (!string.IsNullOrEmpty(result.ServerDetail))
                Console.WriteLine("       服务端信息：" + Snip(result.ServerDetail));
            return 1;
        }

        var data = result.Data!;
        Console.WriteLine($"[通过] 额度接口解析成功 · 套餐等级：{data.Level ?? "未返回"}");
        Console.WriteLine();

        Console.WriteLine("额度（limits，接口返回几条列几条；标签按 ZCode 三元组映射）：");
        if (data.Limits.Count == 0)
            Console.WriteLine("  （接口未返回 limits）");
        foreach (var limit in data.Limits)
        {
            Console.WriteLine($"  {GlmQuotaCard.GlmLimitLabel(limit)}  type={limit.Type} unit={N(limit.Unit)} number={N(limit.Number)}"
                + $" 上限={N(limit.Usage)} 已用={N(limit.CurrentValue)} 剩余={N(limit.Remaining)} 已用百分比={N(limit.Percentage)}"
                + $" 重置={limit.NextResetTime?.ToLocalTime():yyyy-MM-dd HH:mm}（本地时间）");
        }

        Console.WriteLine();
        if (provider == GlmProvider.BigModel)
        {
            Console.WriteLine("MCP 额度：BigModel 未提供该接口（/api/v1/mcp/usage 返回 404）；调用次数见下方工具明细。");
        }
        else
        {
            Console.WriteLine("MCP 额度（/api/v1/mcp/usage）：");
            if (data.McpQuota is { } mcp)
                Console.WriteLine($"  已用={N(mcp.Used)} 上限={N(mcp.Limit)} 剩余={N(mcp.Remaining)} 等级={mcp.Level ?? "—"} 下次刷新={mcp.NextRefreshAt?.ToLocalTime():yyyy-MM-dd HH:mm}");
            else
                Console.WriteLine("  （未获取：见下）");
        }

        Console.WriteLine();
        Console.WriteLine($"模型 Token 消耗（BigModel=/api/monitor/usage/model-usage；Z.ai=credit-usage?usageType=MODEL）：共 {data.Models.Count} 个模型");
        foreach (var model in data.Models)
            Console.WriteLine($"  {model.ModelName,-22} 缓存输入={N(model.CachedInputTokens)} 输入={N(model.UncachedInputTokens)}"
                + $" 输出={N(model.OutputTokens)} 合计={N(model.TotalTokens)} 积分={N(model.TotalCredits)}");
        Console.WriteLine($"  区间总 Token：{N(data.TotalTokens)} · 区间总模型调用：{N(data.TotalModelCallCount)} 次");

        Console.WriteLine();
        Console.WriteLine($"MCP 工具调用明细：共 {data.McpTools.Count} 项，总调用 {N(data.TotalMcpCallCount)} 次");
        foreach (var tool in data.McpTools)
            Console.WriteLine($"  {tool.Name,-22} 调用={N(tool.CallCount)} 积分={N(tool.TotalCredits)}");

        Console.WriteLine();
        Console.WriteLine($"统计范围：{data.RangeStart?.ToLocalTime():yyyy-MM-dd} ~ {data.RangeEnd?.ToLocalTime():yyyy-MM-dd}");
        Console.WriteLine($"最后更新：{data.FetchedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
        if (!string.IsNullOrEmpty(data.PartialNote))
        {
            Console.WriteLine();
            Console.WriteLine("[注意] " + data.PartialNote);
        }

        // 原始额度响应片段：用于确认字段口径（不含密钥）
        var (status, body, error) = client.FetchRawAsync(provider, key, GlmEndpoints.QuotaPath, null).GetAwaiter().GetResult();
        Console.WriteLine();
        Console.WriteLine($"原始额度响应（HTTP {status}）片段：");
        Console.WriteLine("  " + Snip(body ?? error ?? "(空)"));

        Console.WriteLine();
        Console.WriteLine("请与 ZCode「使用统计 → 编程套餐」逐项对比：5 小时剩余 / 每周剩余 / 工具调用次数 / 各模型 Token 应一致。");
        return 0;
    }

    private static string N(double? value) => value?.ToString("0.##") ?? "—";

    private static string Snip(string body)
    {
        var oneLine = body.Replace("\r", "").Replace("\n", " ");
        return oneLine.Length <= 600 ? oneLine : oneLine[..600] + "…";
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
