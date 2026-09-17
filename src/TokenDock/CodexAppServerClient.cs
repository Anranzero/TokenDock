using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace TokenDock;

/// <summary>
/// 与本地 Codex CLI 的 app-server 通信：以子进程 stdio 承载 JSONL 协议。
/// 生命周期只管理自己启动的子进程（按 PID 结束，不触碰其他进程）。
/// 打包方案：Codex 是独立产品（Rust 二进制，随 Node 安装），不随本 EXE 分发；
/// 本客户端只检测并连接已安装的 `codex`（PATH / npm 全局目录）。
/// </summary>
public sealed class CodexAppServerClient : IDisposable
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);

    private readonly object _sync = new();
    private readonly Dictionary<int, TaskCompletionSource<JsonElement>> _pending = new();
    private Process? _process;
    private StreamWriter? _stdin;
    private Task? _readLoop;
    private int _nextId = 1;
    private bool _disposed;

    /// <summary>收到 account/rateLimits/updated 通知（payload 能解析为快照时携带数据）。</summary>
    public event Action<CodexRateLimitsSnapshot?>? RateLimitsUpdated;

    /// <summary>子进程退出（含被外部结束）。</summary>
    public event Action? ProcessExited;

    public bool IsRunning
    {
        get
        {
            lock (_sync)
            {
                return _process is { HasExited: false };
            }
        }
    }

    /// <summary>在 PATH 与 npm 全局目录中查找 codex 可执行文件；未安装返回 null。</summary>
    public static string? LocateExecutable()
    {
        var names = new[] { "codex.exe", "codex.cmd", "codex.bat", "codex" };
        var directories = new List<string>();
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var part in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = part.Trim().Trim('"');
            if (trimmed.Length > 0) directories.Add(trimmed);
        }

        var npmGlobal = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm");
        if (Directory.Exists(npmGlobal)) directories.Insert(0, npmGlobal);

        foreach (var dir in directories)
        {
            foreach (var name in names)
            {
                var candidate = Path.Combine(dir, name);
                if (File.Exists(candidate)) return candidate;
            }
        }

        return null;
    }

    /// <summary>启动 app-server 并完成 initialize → initialized 握手。</summary>
    public async Task EnsureReadyAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning) return;

        var executable = LocateExecutable()
            ?? throw new CodexException(CodexFailureKind.NotInstalled,
                "未检测到 Codex CLI。请先安装：npm i -g @openai/codex（需 Node.js 18+）");

        var startInfo = new ProcessStartInfo
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardInputEncoding = new UTF8Encoding(false),
        };

        // Windows 上 codex 通常是 npm 的 .cmd 外壳，必须经 cmd.exe 启动
        if (executable.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase)
            || executable.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
        {
            startInfo.FileName = "cmd.exe";
            startInfo.Arguments = $"/c \"\"{executable}\" app-server\"";
        }
        else
        {
            startInfo.FileName = executable;
            startInfo.Arguments = "app-server";
        }

        Process process;
        try
        {
            process = Process.Start(startInfo)
                ?? throw new CodexException(CodexFailureKind.StartFailed, "无法启动 Codex app-server 进程。");
        }
        catch (Exception ex) when (ex is not CodexException)
        {
            throw new CodexException(CodexFailureKind.StartFailed, "启动 Codex app-server 失败：" + ex.Message, ex);
        }

        lock (_sync)
        {
            _process = process;
            _stdin = process.StandardInput;
            _readLoop = Task.Run(() => ReadLoopAsync(process.StandardOutput));
        }

        process.EnableRaisingEvents = true;
        process.Exited += (_, _) =>
        {
            lock (_sync)
            {
                foreach (var pending in _pending.Values)
                    pending.TrySetException(new CodexException(CodexFailureKind.ProcessExited, "Codex 进程已退出。"));
                _pending.Clear();
            }
            ProcessExited?.Invoke();
        };

        // 丢弃 stderr，避免管道写满阻塞子进程
        _ = Task.Run(async () =>
        {
            try
            {
                while (await process.StandardError.ReadLineAsync() is not null) { }
            }
            catch (Exception)
            {
                // 进程退出时忽略
            }
        });

        var initializeResult = await SendRequestAsync(
            CodexProtocol.BuildInitialize(NextId(), "tokendock", ClientVersion()), cancellationToken);
        _ = initializeResult;
        await SendNotificationAsync(CodexProtocol.BuildInitialized(), cancellationToken);
    }

    private static string ClientVersion() => "2.0.2";

    public async Task<CodexAccountInfo> AccountReadAsync(CancellationToken cancellationToken = default)
        => CodexProtocol.ParseAccount(await SendRequestAsync(CodexProtocol.BuildAccountRead(NextId()), cancellationToken));

    public async Task<(string? LoginId, string? AuthUrl)> LoginStartAsync(CancellationToken cancellationToken = default)
    {
        var result = await SendRequestAsync(CodexProtocol.BuildLoginStart(NextId()), cancellationToken);
        var (loginId, authUrl, _) = CodexProtocol.ParseLoginStart(result);
        if (string.IsNullOrWhiteSpace(authUrl))
            throw new CodexException(CodexFailureKind.LoginFailed, "登录接口未返回授权网址。");
        return (loginId, authUrl);
    }

    public async Task<CodexRateLimitsSnapshot> RateLimitsReadAsync(CancellationToken cancellationToken = default)
        => CodexProtocol.ParseRateLimits(await SendRequestAsync(CodexProtocol.BuildRateLimitsRead(NextId()), cancellationToken));

    private int NextId()
    {
        lock (_sync)
        {
            return _nextId++;
        }
    }

    private async Task<JsonElement> SendRequestAsync(string line, CancellationToken cancellationToken)
    {
        var id = ExtractId(line);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_sync)
        {
            if (_stdin is null)
                throw new CodexException(CodexFailureKind.StartFailed, "Codex 进程尚未启动。");
            _pending[id] = completion;
        }

        try
        {
            await _stdin.WriteLineAsync(line.AsMemory(), cancellationToken);
            await _stdin.FlushAsync(cancellationToken);

            using var timeout = new CancellationTokenSource(RequestTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            await using var registration = linked.Token.Register(() =>
            {
                if (timeout.IsCancellationRequested)
                    completion.TrySetException(new CodexException(CodexFailureKind.Timeout, "请求超时（20 秒），Codex 未响应。"));
                else
                    completion.TrySetCanceled(cancellationToken);
            }).ConfigureAwait(false);

            return await completion.Task.ConfigureAwait(false);
        }
        finally
        {
            lock (_sync)
            {
                _pending.Remove(id);
            }
        }
    }

    private async Task SendNotificationAsync(string line, CancellationToken cancellationToken)
    {
        StreamWriter? stdin;
        lock (_sync)
        {
            stdin = _stdin;
        }
        if (stdin is null)
            throw new CodexException(CodexFailureKind.StartFailed, "Codex 进程尚未启动。");
        await stdin.WriteLineAsync(line.AsMemory(), cancellationToken);
        await stdin.FlushAsync(cancellationToken);
    }

    private static int ExtractId(string line)
    {
        using var doc = JsonDocument.Parse(line);
        if (doc.RootElement.TryGetProperty("id", out var id) && id.TryGetInt32(out var value))
            return value;
        throw new CodexException(CodexFailureKind.Protocol, "请求缺少 id 字段。");
    }

    private async Task ReadLoopAsync(StreamReader reader)
    {
        try
        {
            while (await reader.ReadLineAsync() is { } line)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                DispatchLine(line);
            }
        }
        catch (Exception)
        {
            // 进程退出或管道关闭；由 Exited 事件统一收尾
        }
    }

    private void DispatchLine(string line)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(line);
        }
        catch (JsonException)
        {
            return; // 非 JSON 行（横幅/日志）直接忽略
        }

        using (doc)
        {
            var root = doc.RootElement;

            if (root.TryGetProperty("id", out var idElement) && idElement.TryGetInt32(out var id)
                && (root.TryGetProperty("result", out var result) || root.TryGetProperty("error", out var error)))
            {
                TaskCompletionSource<JsonElement>? completion;
                lock (_sync)
                {
                    _pending.TryGetValue(id, out completion);
                }

                if (completion is null) return;

                if (root.TryGetProperty("error", out var err))
                {
                    completion.TrySetException(new CodexException(CodexFailureKind.RpcError, CodexProtocol.DescribeError(err)));
                }
                else
                {
                    completion.TrySetResult(result.Clone());
                }
                return;
            }

            // 通知（无 id）
            if (root.TryGetProperty("method", out var method) && method.ValueKind == JsonValueKind.String
                && method.GetString() == CodexProtocol.MethodRateLimitsUpdated)
            {
                CodexRateLimitsSnapshot? snapshot = null;
                if (root.TryGetProperty("params", out var prms))
                {
                    try
                    {
                        snapshot = CodexProtocol.ParseRateLimits(prms);
                    }
                    catch (CodexException)
                    {
                        snapshot = null; // payload 结构不同则仅提示需要刷新
                    }
                }
                RateLimitsUpdated?.Invoke(snapshot);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Process? process;
        StreamWriter? stdin;
        lock (_sync)
        {
            process = _process;
            stdin = _stdin;
            _process = null;
            _stdin = null;
            _pending.Clear();
        }

        try
        {
            stdin?.Dispose();
        }
        catch (Exception)
        {
            // 忽略关闭管道异常
        }

        if (process is not null)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch (Exception)
            {
                // 进程可能已自行退出
            }
            process.Dispose();
        }
    }
}
