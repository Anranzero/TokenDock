using System.Globalization;
using Microsoft.Data.Sqlite;

namespace TokenDock;

/// <summary>单条本机会话用量记录（assistant 消息，按消息 ID 去重后）。</summary>
public sealed class TokenUsageRecord
{
    public string MessageId { get; init; } = "";
    public string Model { get; init; } = "";
    public DateTimeOffset CreatedUtc { get; init; }
    public long Input { get; init; }
    public long Output { get; init; }
    public long Reasoning { get; init; }
    public long CacheRead { get; init; }
    public long CacheWrite { get; init; }
    public long Total { get; init; }
}

/// <summary>
/// 某模型的 Token 聚合。Output 口径与 `opencode stats --models` 一致（output + reasoning），
/// 这是经逐模型核对确认的（deepseek/kimi 等模型的推理 token 计入输出）。
/// </summary>
public sealed class ModelTokenTotals
{
    public string Model { get; init; } = "";
    public long Messages { get; init; }
    public long Input { get; init; }
    public long Output { get; init; }
    public long CacheRead { get; init; }
    public long CacheWrite { get; init; }
    public long Total { get; init; }
    public long Cache => CacheRead + CacheWrite;
}

/// <summary>Token 统计的纯计算层（聚合、窗口切片、去重、格式化）。</summary>
public static class TokenUsageMath
{
    /// <summary>本机 OpenCode 数据中，属于 OpenCode Go 的 providerID。</summary>
    public const string GoProviderId = "opencode-go";

    /// <summary>按消息 ID 去重（同一客户端多个会话库中的重复记录只保留一条）。</summary>
    public static IReadOnlyList<TokenUsageRecord> Dedupe(IEnumerable<TokenUsageRecord> records)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<TokenUsageRecord>();
        foreach (var record in records)
        {
            if (seen.Add(record.MessageId))
                result.Add(record);
        }
        return result;
    }

    /// <summary>按模型聚合，按总 Token 降序。</summary>
    public static IReadOnlyList<ModelTokenTotals> Aggregate(IEnumerable<TokenUsageRecord> records)
    {
        var sums = new Dictionary<string, long[]>(StringComparer.Ordinal); // msgs, in, out, reasoning, cacheR, cacheW, total
        foreach (var record in records)
        {
            if (!sums.TryGetValue(record.Model, out var acc))
            {
                acc = new long[7];
                sums[record.Model] = acc;
            }
            acc[0]++;
            acc[1] += record.Input;
            acc[2] += record.Output;
            acc[3] += record.Reasoning;
            acc[4] += record.CacheRead;
            acc[5] += record.CacheWrite;
            acc[6] += record.Total;
        }

        return sums
            .Select(pair => new ModelTokenTotals
            {
                Model = pair.Key,
                Messages = pair.Value[0],
                Input = pair.Value[1],
                Output = pair.Value[2] + pair.Value[3], // 与 opencode stats 口径一致：输出含推理
                CacheRead = pair.Value[4],
                CacheWrite = pair.Value[5],
                Total = pair.Value[6],
            })
            .OrderByDescending(t => t.Total)
            .ToList();
    }

    /// <summary>Zcode model_usage 行 → 记录；status 非 completed（失败/取消）返回 null 不统计。</summary>
    public static TokenUsageRecord? FromZcodeRow(
        string id, string model, long startedAtMs, string? status,
        long input, long output, long reasoning, long cacheRead, long cacheWrite,
        long providerTotal, long computedTotal)
    {
        if (!string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase)) return null;

        var total = providerTotal != 0 ? providerTotal : computedTotal;
        if (total == 0) total = input + output + reasoning + cacheRead + cacheWrite;

        return new TokenUsageRecord
        {
            MessageId = "zc:" + id,
            Model = model,
            CreatedUtc = DateTimeOffset.FromUnixTimeMilliseconds(startedAtMs),
            Input = input,
            Output = output,
            Reasoning = reasoning,
            CacheRead = cacheRead,
            CacheWrite = cacheWrite,
            Total = total,
        };
    }

    /// <summary>紧凑 Token 数字（与 opencode stats 风格一致）：568 / 913.5K / 2.1M / 1.2B。</summary>
    public static string FormatTokens(long value)
    {
        if (value >= 1_000_000_000) return (value / 1_000_000_000.0).ToString("0.#", CultureInfo.InvariantCulture) + "B";
        if (value >= 1_000_000) return (value / 1_000_000.0).ToString("0.#", CultureInfo.InvariantCulture) + "M";
        if (value >= 1_000) return (value / 1_000.0).ToString("0.#", CultureInfo.InvariantCulture) + "K";
        return value.ToString(CultureInfo.InvariantCulture);
    }
}

/// <summary>
/// 单个客户端的本机采集结果。各客户端完全独立：不跨客户端合并，
/// 也不能用某一客户端的记录缺失推断“账号用量为 0”。
/// </summary>
public sealed class ClientTokenUsage
{
    public string ClientName { get; init; } = "";

    /// <summary>数据来源（会话库位置与数量，供 UI 注明）。</summary>
    public string Source { get; init; } = "";

    /// <summary>本机是否读到该客户端的会话库；与“范围内有无记录”是两回事。</summary>
    public bool Available { get; init; }

    /// <summary>未发现数据 / 读取失败时的原因。</summary>
    public string Detail { get; init; } = "";

    public int DatabaseCount { get; init; }
    public int FailedDatabaseCount { get; init; }
    public IReadOnlyList<TokenUsageRecord> Records { get; init; } = Array.Empty<TokenUsageRecord>();

    /// <summary>本机记录实际覆盖的时间范围（可能远窄于账号真实使用范围）。</summary>
    public DateTimeOffset? EarliestUtc { get; init; }
    public DateTimeOffset? LatestUtc { get; init; }

    public static ClientTokenUsage Unavailable(string clientName, string source, string detail)
        => new() { ClientName = clientName, Source = source, Available = false, Detail = detail };
}

/// <summary>
/// 一次本机 Token 采集的结果：按客户端分组，只描述各客户端本机留存的数据。
/// 官方接口（/zen/go/v1/usage）暂未提供账号级逐请求/逐模型 Token 历史，
/// 因此这里没有、也不构造“账号总量”。
/// </summary>
public sealed class TokenUsageReport
{
    /// <summary>任一客户端读到数据即为 true；false 时 Clients 为空、Detail 说明原因。</summary>
    public bool Available { get; init; }
    public string Detail { get; init; } = "";

    /// <summary>各客户端独立结果（OpenCode / Zcode），顺序固定，不合并。</summary>
    public IReadOnlyList<ClientTokenUsage> Clients { get; init; } = Array.Empty<ClientTokenUsage>();

    public DateTimeOffset ScannedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public static TokenUsageReport Unavailable(string detail)
        => new() { Available = false, Detail = detail };
}

/// <summary>
/// 本机会话库读取（按客户端独立采集，绝不合并成“账号总量”）：
/// - OpenCode 客户端：只读扫描 ~/.local/share/opencode/opencode*.db，
///   仅取 providerID = "opencode-go" 的 assistant 消息，SQL 层 json_extract 过滤，按消息 ID 去重。
/// - Zcode 客户端：只读读取 ~/.zcode/cli/db/db.sqlite 的 model_usage 表（provider 以 opencode-go 开头）。
/// 全程只读，绝不读取登录令牌等敏感字段。
/// </summary>
public static class TokenUsageReader
{
    public static string DataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "opencode");

    /// <summary>Zcode 客户端的会话库（其 model_usage 表记录了每次请求的 Token，含 OpenCode Go）。</summary>
    public static string ZcodeDatabasePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".zcode", "cli", "db", "db.sqlite");

    public static TokenUsageReport Scan()
    {
        try
        {
            // 每个客户端独立扫描、独立去重、独立给出来源与覆盖范围
            var clients = new[] { ScanOpenCodeClient(), ScanZcodeClient() };
            if (clients.All(c => !c.Available))
                return TokenUsageReport.Unavailable("未发现本机 OpenCode/Zcode 会话数据，或会话库均读取失败");

            return new TokenUsageReport
            {
                Available = true,
                Detail = "本机客户端会话库只读采集（仅 provider=" + TokenUsageMath.GoProviderId + "*）",
                Clients = clients,
            };
        }
        catch (Exception ex)
        {
            return TokenUsageReport.Unavailable("采集失败：" + ex.Message);
        }
    }

    private static ClientTokenUsage ScanOpenCodeClient()
    {
        const string clientName = "OpenCode";
        var source = DataDirectory;
        if (!Directory.Exists(source))
            return ClientTokenUsage.Unavailable(clientName, source, "未发现会话数据目录");

        var databases = Directory
            .GetFiles(source, "opencode*.db")
            .Where(path => !path.EndsWith("-wal", StringComparison.OrdinalIgnoreCase)
                           && !path.EndsWith("-shm", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (databases.Length == 0)
            return ClientTokenUsage.Unavailable(clientName, source, "目录中未发现会话库");

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var records = new List<TokenUsageRecord>();
        var failed = 0;
        foreach (var database in databases)
        {
            try
            {
                ReadDatabase(database, seen, records);
            }
            catch (Exception)
            {
                failed++; // 单库读取失败（被占用等）不影响其余库
            }
        }

        if (failed == databases.Length)
            return ClientTokenUsage.Unavailable(clientName, source, "会话库读取失败（可能被其他程序占用）");

        return BuildClient(clientName, databases.Length, failed, records);
    }

    private static ClientTokenUsage ScanZcodeClient()
    {
        const string clientName = "Zcode";
        var source = ZcodeDatabasePath;
        if (!File.Exists(source))
            return ClientTokenUsage.Unavailable(clientName, source, "未发现会话库");

        try
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var records = new List<TokenUsageRecord>();
            ReadZcodeDatabase(source, seen, records);
            return BuildClient(clientName, databases: 1, failed: 0, records);
        }
        catch (Exception)
        {
            return ClientTokenUsage.Unavailable(clientName, source, "会话库读取失败（可能被其他程序占用）");
        }
    }

    private static ClientTokenUsage BuildClient(string clientName, int databases, int failed, List<TokenUsageRecord> records)
    {
        return new ClientTokenUsage
        {
            ClientName = clientName,
            Source = clientName == "OpenCode"
                ? $"~/.local/share/opencode（{databases} 个会话库）"
                : "~/.zcode/cli/db/db.sqlite",
            Available = true,
            Detail = failed > 0 ? $"{failed} 个库读取失败" : "",
            DatabaseCount = databases,
            FailedDatabaseCount = failed,
            Records = records,
            EarliestUtc = records.Count == 0 ? null : records.Min(r => r.CreatedUtc),
            LatestUtc = records.Count == 0 ? null : records.Max(r => r.CreatedUtc),
        };
    }

    private static void ReadDatabase(string path, HashSet<string> seen, List<TokenUsageRecord> records)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString();

        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, time_created,
                   json_extract(data,'$.modelID'),
                   json_extract(data,'$.tokens.input'),
                   json_extract(data,'$.tokens.output'),
                   json_extract(data,'$.tokens.reasoning'),
                   json_extract(data,'$.tokens.cache.read'),
                   json_extract(data,'$.tokens.cache.write'),
                   json_extract(data,'$.tokens.total')
            FROM message
            WHERE json_extract(data,'$.providerID') = $provider
            """;
        command.Parameters.AddWithValue("$provider", TokenUsageMath.GoProviderId);

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var id = reader.GetString(0);
            if (!seen.Add(id)) continue; // 跨库按消息 ID 去重

            records.Add(new TokenUsageRecord
            {
                MessageId = id,
                Model = reader.IsDBNull(2) ? "未知模型" : reader.GetString(2),
                CreatedUtc = DateTimeOffset.FromUnixTimeMilliseconds(AsLong(reader.GetValue(1))),
                Input = AsLong(reader.GetValue(3)),
                Output = AsLong(reader.GetValue(4)),
                Reasoning = AsLong(reader.GetValue(5)),
                CacheRead = AsLong(reader.GetValue(6)),
                CacheWrite = AsLong(reader.GetValue(7)),
                Total = AsLong(reader.GetValue(8)),
            });
        }
    }

    /// <summary>
    /// Zcode 会话库：model_usage 表中 provider 以 opencode-go 开头的已完成请求。
    /// 该表由 Zcode 自身记录每次模型请求的 Token（含 OpenCode Go 的用量），按行 id 去重。
    /// </summary>
    private static void ReadZcodeDatabase(string path, HashSet<string> seen, List<TokenUsageRecord> records)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString();

        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, model_id, started_at, status,
                   input_tokens, output_tokens, reasoning_tokens,
                   cache_read_input_tokens, cache_creation_input_tokens,
                   provider_total_tokens, computed_total_tokens
            FROM model_usage
            WHERE provider_id LIKE 'opencode-go%'
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var mapped = TokenUsageMath.FromZcodeRow(
                reader.GetString(0),
                reader.IsDBNull(1) ? "未知模型" : reader.GetString(1),
                AsLong(reader.GetValue(2)),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                AsLong(reader.GetValue(4)),
                AsLong(reader.GetValue(5)),
                AsLong(reader.GetValue(6)),
                AsLong(reader.GetValue(7)),
                AsLong(reader.GetValue(8)),
                AsLong(reader.GetValue(9)),
                AsLong(reader.GetValue(10)));
            if (mapped is null) continue;
            if (seen.Add(mapped.MessageId)) records.Add(mapped);
        }
    }

    private static long AsLong(object value) => value switch
    {
        long l => l,
        double d => (long)d,
        string s when long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
        _ => 0,
    };
}

/// <summary>
/// 解析 `opencode stats --models` 文本输出中的 opencode-go 模型行，用于与官方统计交叉核对。
/// （输出形如 "│ opencode-go/deepseek-v4-flash" + "│  Input Tokens  2.1M │" 等行）
/// </summary>
public static class OpenCodeStatsParser
{
    public sealed class CliModelStats
    {
        public string Model { get; init; } = "";
        public long Messages { get; init; }
        public double Input { get; init; }
        public double Output { get; init; }
        public double CacheRead { get; init; }
        public double CacheWrite { get; init; }
    }

    /// <summary>
    /// 解析 `opencode stats --models` 文本输出中的 opencode-go 模型行。
    /// 关键：任何“非统计字段”的行都被视为新的模型区块起点——否则后续非 Go 模型的数字
    /// 会串到前一个 Go 模型上（已在实现中修复并加了回归测试）。
    /// </summary>
    public static IReadOnlyDictionary<string, CliModelStats> ParseGoModels(string cliOutput)
    {
        var result = new Dictionary<string, CliModelStats>(StringComparer.Ordinal);
        string? currentModel = null;
        var fields = new Dictionary<string, double>(StringComparer.Ordinal);
        long messages = 0;

        void Flush()
        {
            if (currentModel is null) return;
            result[currentModel] = new CliModelStats
            {
                Model = currentModel,
                Messages = messages,
                Input = fields.GetValueOrDefault("Input Tokens"),
                Output = fields.GetValueOrDefault("Output Tokens"),
                CacheRead = fields.GetValueOrDefault("Cache Read"),
                CacheWrite = fields.GetValueOrDefault("Cache Write"),
            };
            fields.Clear();
            messages = 0;
        }

        foreach (var rawLine in cliOutput.Split('\n'))
        {
            var line = rawLine.Trim().Trim('│').Trim();
            if (line.Length == 0) continue;

            var fieldValue = MatchField(line);
            if (fieldValue is { } field)
            {
                if (currentModel is null) continue;
                if (field.Name == "Messages") messages = (long)ParseValue(field.Value);
                else fields[field.Name] = ParseValue(field.Value);
                continue;
            }

            // 非统计字段行 = 新模型区块（或表头/边框）：结束当前区块
            Flush();
            currentModel = line.Contains("opencode-go/", StringComparison.Ordinal)
                ? line[(line.IndexOf("opencode-go/", StringComparison.Ordinal) + "opencode-go/".Length)..].Trim()
                : null;
        }

        Flush();
        return result;
    }

    /// <summary>识别统计字段行（"Messages 982" / "Input Tokens 2.1M" ...），返回字段名与值。</summary>
    private static (string Name, string Value)? MatchField(string line)
    {
        foreach (var name in new[] { "Messages", "Input Tokens", "Output Tokens", "Cache Read", "Cache Write", "Cost" })
        {
            if (!line.StartsWith(name, StringComparison.Ordinal)) continue;
            var value = line[name.Length..].Trim();
            // 必须是数值形式的字段行（排除表头等）
            if (value.Length == 0 || (!char.IsDigit(value[0]) && value[0] != '$')) return null;
            return (name, value);
        }

        return null;
    }

    /// <summary>解析 "2.1M" / "913.5K" / "982" / "1.1B" 形式的数值。</summary>
    public static double ParseValue(string text)
    {
        var value = text.Trim().Replace(",", "");
        if (value.Length == 0) return 0;

        var multiplier = 1d;
        var last = value[^1];
        if (char.IsLetter(last))
        {
            multiplier = char.ToUpperInvariant(last) switch
            {
                'K' => 1_000d,
                'M' => 1_000_000d,
                'B' => 1_000_000_000d,
                _ => 1d,
            };
            value = value[..^1];
        }

        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            ? number * multiplier
            : 0;
    }

    /// <summary>
    /// CLI 数值与本地聚合是否一致：CLI 以 K/M/B 保留 1 位小数展示，
    /// 因此容差取“展示单位的一半”（如 2.1M ± 0.051M）。
    /// </summary>
    public static bool ApproximatelyEqual(double cliValue, long localValue)
    {
        var unit = cliValue >= 1_000_000_000 ? 1_000_000_000d
            : cliValue >= 1_000_000 ? 1_000_000d
            : cliValue >= 1_000 ? 1_000d
            : 1d;
        return Math.Abs(cliValue - localValue) <= unit * 0.051;
    }
}
