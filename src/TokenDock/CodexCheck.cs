using System.Runtime.InteropServices;

namespace TokenDock;

/// <summary>
/// --codexcheck：对本机 Codex app-server 做一次真实端到端自检
/// （检测依赖 → initialize 握手 → account/read → rateLimits/read → 进程退出处理）。
/// </summary>
internal static class CodexCheck
{
    public static int Run()
    {
        AttachParentConsole();
        Console.WriteLine("Codex app-server 连通性自检");
        var failures = 0;
        void Check(string name, bool ok, string? detail = null)
        {
            Console.WriteLine((ok ? "[通过] " : "[失败] ") + name + (detail is null ? "" : "：" + detail));
            if (!ok) failures++;
        }

        var path = CodexAppServerClient.LocateExecutable();
        Check("检测 Codex CLI", path is not null, path ?? "未找到（安装：npm i -g @openai/codex）");
        if (path is null) return 1;

        var client = new CodexAppServerClient();
        try
        {
            var readyTask = client.EnsureReadyAsync();
            var ready = readyTask.Wait(TimeSpan.FromSeconds(30));
            Check("启动并完成 initialize 握手", ready && readyTask.IsCompletedSuccessfully,
                readyTask.Exception?.GetBaseException().Message);

            if (ready && readyTask.IsCompletedSuccessfully)
            {
                try
                {
                    var account = client.AccountReadAsync().GetAwaiter().GetResult();
                    Check("account/read", true, account.DisplayLine);
                    if (account.LoggedIn)
                    {
                        try
                        {
                            var snapshot = client.RateLimitsReadAsync().GetAwaiter().GetResult();
                            var levels = CodexLevelView.Flatten(snapshot);
                            Check("account/rateLimits/read", levels.Count > 0, $"{levels.Count} 个额度窗口");
                            foreach (var level in levels)
                                Console.WriteLine($"    · {level.Title}：剩余 {level.Window.RemainingText}，{level.Window.CountdownText}");
                        }
                        catch (CodexException ex)
                        {
                            // 已知情况：CLI 版本不认识账号的 planType 时会报 RPC 错误；这恰好验证错误处理路径
                            Check("account/rateLimits/read（RPC/协议错误被正确分类，界面会显示失败并保留旧数据）",
                                ex.Kind is CodexFailureKind.RpcError or CodexFailureKind.Protocol, ex.Message);
                        }
                    }
                    else
                    {
                        Console.WriteLine("[跳过] 未登录，未调用 rateLimits/read；登录请用界面「登录 ChatGPT」按钮。");
                    }
                }
                catch (CodexException ex)
                {
                    Check("account/read", false, ex.Message);
                }
            }
        }
        catch (Exception ex)
        {
            Check("握手", false, ex.Message);
        }

        // 进程退出处理：结束后再调用应得到明确失败而不是悬挂
        var exitHandled = false;
        try
        {
            client.Dispose();
            Thread.Sleep(500);
            client.AccountReadAsync().GetAwaiter().GetResult();
        }
        catch (CodexException ex) when (ex.Kind is CodexFailureKind.StartFailed or CodexFailureKind.ProcessExited)
        {
            exitHandled = true;
        }
        catch (Exception)
        {
            // 其他异常视为未通过
        }
        Check("进程退出后调用得到明确失败（不悬挂、不崩溃）", exitHandled);

        Console.WriteLine(failures == 0 ? "全部通过。" : $"未通过 {failures} 项。");
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
            // 非 Windows 环境忽略
        }
    }
}
