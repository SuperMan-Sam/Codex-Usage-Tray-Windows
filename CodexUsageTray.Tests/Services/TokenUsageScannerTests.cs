using CodexUsageTray.Core.Models;
using CodexUsageTray.Core.Services.TokenMeter;
using Microsoft.Data.Sqlite;

namespace CodexUsageTray.Tests.Services;

[TestClass]
public sealed class TokenUsageScannerTests
{
    [TestMethod]
    public async Task ScanAsync_WithMinimalFixtureForEveryClient_ParsesAllClients()
    {
        using TempWorkspace workspace = new();
        TokenUsageScanOptions options = workspace.CreateOptions();

        foreach (TokenClientDefinition client in TokenClientCatalog.All)
        {
            workspace.WriteFixture(client, options);
        }

        TokenUsageScanner scanner = new();
        TokenUsageScanResult result = await scanner.ScanAsync(options);

        CollectionAssert.AreEquivalent(
            TokenClientCatalog.All.Select(client => client.Id).ToArray(),
            result.Entries.Select(entry => entry.Client).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
        Assert.AreEqual(TokenClientCatalog.All.Count, result.Entries.Count);
        Assert.AreEqual(TokenClientCatalog.All.Count, result.ScannedFileCount);
        Assert.AreEqual(0, result.FailedFileCount);
        Assert.IsTrue(result.Entries.All(entry => entry.Tokens.Total > 0));
        Assert.IsTrue(result.Entries.All(entry => entry.Model == "gpt-5.3-codex"));
    }

    [TestMethod]
    public async Task ScanAsync_WhenOneFileIsCorrupt_KeepsValidEntriesAndCountsFailure()
    {
        using TempWorkspace workspace = new();
        TokenUsageScanOptions options = workspace.CreateOptions();
        TokenClientDefinition codex = TokenClientCatalog.All.First(client => client.Id == "codex");
        workspace.WriteFixture(codex, options);
        string corruptPath = Path.Combine(Path.GetDirectoryName(workspace.GetFixturePath(codex, options))!, "corrupt.jsonl");
        File.WriteAllText(corruptPath, "{");

        TokenUsageScanner scanner = new();
        TokenUsageScanResult result = await scanner.ScanAsync(options);

        Assert.AreEqual(1, result.Entries.Count);
        Assert.AreEqual(2, result.ScannedFileCount);
        Assert.AreEqual(0, result.FailedFileCount, "Corrupt JSONL rows are skipped so the file can still contribute later valid rows.");

        TokenClientDefinition gemini = TokenClientCatalog.All.First(client => client.Id == "gemini");
        workspace.WriteFixture(gemini, options);
        string corruptJson = Path.Combine(Path.GetDirectoryName(workspace.GetFixturePath(gemini, options))!, "broken.json");
        File.WriteAllText(corruptJson, "{");

        result = await scanner.ScanAsync(options);

        Assert.IsTrue(result.Entries.Count >= 2);
        Assert.AreEqual(1, result.FailedFileCount);
    }

    [TestMethod]
    public async Task ScanAsync_WithCodexSession_IgnoresNestedToolOutputTokenObjects()
    {
        using TempWorkspace workspace = new();
        TokenUsageScanOptions options = workspace.CreateOptions();

        workspace.WriteCodexSession(options, """
            {"timestamp":"2026-05-31T10:00:00+10:00","type":"session_meta","payload":{"id":"session-codex","cwd":"workspace","model_provider":"openai"}}
            {"timestamp":"2026-05-31T10:00:01+10:00","type":"turn_context","payload":{"model":"gpt-5.3-codex","cwd":"workspace"}}
            {"timestamp":"2026-05-31T10:00:02+10:00","type":"response_item","payload":{"type":"tool_output","content":[{"tokens":{"input_tokens":999999,"output_tokens":999999}}]}}
            {"timestamp":"2026-05-31T10:00:03+10:00","type":"event_msg","payload":{"type":"token_count","info":{"last_token_usage":{"input_tokens":100,"output_tokens":20,"cached_input_tokens":10,"reasoning_output_tokens":2}}}}
            """);

        TokenUsageScanner scanner = new();
        TokenUsageScanResult result = await scanner.ScanAsync(options);

        Assert.AreEqual(1, result.Entries.Count);
        Assert.AreEqual("codex", result.Entries[0].Client);
        Assert.AreEqual("gpt-5.3-codex", result.Entries[0].Model);
        Assert.AreEqual("workspace", result.Entries[0].WorkspaceLabel);
        Assert.AreEqual(122, result.Entries[0].Tokens.Total);
    }

    [TestMethod]
    public async Task ScanAsync_WithCodexCacheReadAlias_UsesLargerCacheField()
    {
        using TempWorkspace workspace = new();
        TokenUsageScanOptions options = workspace.CreateOptions();

        workspace.WriteCodexSession(options, """
            {"timestamp":"2026-05-31T10:00:00+10:00","type":"turn_context","payload":{"model":"gpt-5.5","cwd":"workspace"}}
            {"timestamp":"2026-05-31T10:00:01+10:00","type":"event_msg","payload":{"type":"token_count","info":{"last_token_usage":{"input_tokens":100,"output_tokens":5,"cached_input_tokens":2,"cache_read_input_tokens":20,"reasoning_output_tokens":1}}}}
            """);

        TokenUsageScanner scanner = new();
        TokenUsageScanResult result = await scanner.ScanAsync(options);

        Assert.AreEqual(1, result.Entries.Count);
        Assert.AreEqual(80, result.Entries[0].Tokens.Input);
        Assert.AreEqual(20, result.Entries[0].Tokens.CacheRead);
        Assert.AreEqual(106, result.Entries[0].Tokens.Total);
    }

    [TestMethod]
    public async Task ScanAsync_WithCodexRepeatedTotals_UsesOnlyNewTokenDelta()
    {
        using TempWorkspace workspace = new();
        TokenUsageScanOptions options = workspace.CreateOptions();

        workspace.WriteCodexSession(options, """
            {"timestamp":"2026-05-31T10:00:00+10:00","type":"turn_context","payload":{"model":"gpt-5.3-codex","cwd":"workspace"}}
            {"timestamp":"2026-05-31T10:00:01+10:00","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":100,"output_tokens":20,"cached_input_tokens":10,"reasoning_output_tokens":2},"last_token_usage":{"input_tokens":100,"output_tokens":20,"cached_input_tokens":10,"reasoning_output_tokens":2}}}}
            {"timestamp":"2026-05-31T10:00:02+10:00","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":100,"output_tokens":20,"cached_input_tokens":10,"reasoning_output_tokens":2},"last_token_usage":{"input_tokens":100,"output_tokens":20,"cached_input_tokens":10,"reasoning_output_tokens":2}}}}
            {"timestamp":"2026-05-31T10:00:03+10:00","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":150,"output_tokens":25,"cached_input_tokens":20,"reasoning_output_tokens":3},"last_token_usage":{"input_tokens":50,"output_tokens":5,"cached_input_tokens":10,"reasoning_output_tokens":1}}}}
            """);

        TokenUsageScanner scanner = new();
        TokenUsageScanResult result = await scanner.ScanAsync(options);

        Assert.AreEqual(2, result.Entries.Count);
        Assert.AreEqual(178, result.Entries.Sum(entry => entry.Tokens.Total));
    }

    [TestMethod]
    public async Task ScanAsync_WithCodexResumedSession_UsesLastUsageInsteadOfCarriedTotal()
    {
        using TempWorkspace workspace = new();
        TokenUsageScanOptions options = workspace.CreateOptions();

        workspace.WriteCodexSession(options, """
            {"timestamp":"2026-05-31T10:00:00+10:00","type":"turn_context","payload":{"model":"gpt-5.3-codex","cwd":"workspace"}}
            {"timestamp":"2026-05-31T10:00:01+10:00","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":5000,"cached_input_tokens":500,"output_tokens":800,"reasoning_output_tokens":100},"last_token_usage":{"input_tokens":12,"cached_input_tokens":2,"output_tokens":5,"reasoning_output_tokens":1}}}}
            {"timestamp":"2026-05-31T10:00:02+10:00","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":5012,"cached_input_tokens":502,"output_tokens":805,"reasoning_output_tokens":101},"last_token_usage":{"input_tokens":12,"cached_input_tokens":2,"output_tokens":5,"reasoning_output_tokens":1}}}}
            """);

        TokenUsageScanner scanner = new();
        TokenUsageScanResult result = await scanner.ScanAsync(options);

        Assert.AreEqual(2, result.Entries.Count);
        Assert.AreEqual(36, result.Entries.Sum(entry => entry.Tokens.Total));
    }

    [TestMethod]
    public async Task ScanAsync_WithCodexArchivedSession_IncludesArchiveInCodexClient()
    {
        using TempWorkspace workspace = new();
        TokenUsageScanOptions options = workspace.CreateOptions();

        workspace.WriteCodexSession(options, """
            {"timestamp":"2026-05-31T10:00:00+10:00","type":"turn_context","payload":{"model":"gpt-5.3-codex","cwd":"live-workspace"}}
            {"timestamp":"2026-05-31T10:00:01+10:00","type":"event_msg","payload":{"type":"token_count","info":{"last_token_usage":{"input_tokens":100,"output_tokens":20,"cached_input_tokens":10,"reasoning_output_tokens":2}}}}
            """);
        workspace.WriteCodexArchivedSession(options, """
            {"timestamp":"2026-05-30T10:00:00+10:00","type":"turn_context","payload":{"model":"gpt-5.3-codex","cwd":"archived-workspace"}}
            {"timestamp":"2026-05-30T10:00:01+10:00","type":"event_msg","payload":{"type":"token_count","info":{"last_token_usage":{"input_tokens":50,"output_tokens":5,"cached_input_tokens":10,"reasoning_output_tokens":1}}}}
            """);

        TokenUsageScanner scanner = new();
        TokenUsageScanResult result = await scanner.ScanAsync(options);

        Assert.AreEqual(2, result.Entries.Count);
        Assert.AreEqual(178, result.Entries.Sum(entry => entry.Tokens.Total));
        Assert.IsTrue(result.Entries.All(entry => entry.Client == "codex"));
        Assert.IsTrue(result.Entries.Any(entry => entry.WorkspaceLabel == "archived-workspace"));
    }

    private sealed class TempWorkspace : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "CodexUsageTrayTests", Guid.NewGuid().ToString("N"));

        public TokenUsageScanOptions CreateOptions()
        {
            string home = Path.Combine(_root, "home");
            string xdg = Path.Combine(_root, "xdg-data");
            string config = Path.Combine(_root, "config");
            return new TokenUsageScanOptions
            {
                HomeDirectory = home,
                XdgDataDirectory = xdg,
                ConfigDirectory = config,
                LocalAppDataDirectory = xdg,
                RoamingAppDataDirectory = config,
                EnvironmentOverrides =
                {
                    ["CODEX_HOME"] = Path.Combine(_root, "codex-home"),
                    ["HERMES_HOME"] = Path.Combine(_root, "hermes-home"),
                    ["CODEBUFF_DATA_DIR"] = Path.Combine(_root, "codebuff-home"),
                },
            };
        }

        public string GetFixturePath(TokenClientDefinition client, TokenUsageScanOptions options)
        {
            string configuredPath = options.ResolveClientDirectory(client);
            if (Path.GetFileName(configuredPath).Equals(client.Pattern, StringComparison.OrdinalIgnoreCase))
            {
                return configuredPath;
            }

            return Path.Combine(configuredPath, FixtureFileName(client.Pattern));
        }

        public void WriteFixture(TokenClientDefinition client, TokenUsageScanOptions options)
        {
            string path = GetFixturePath(client, options);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            if (string.Equals(client.Id, "codex", StringComparison.OrdinalIgnoreCase))
            {
                File.WriteAllText(path, CodexJsonLineFixture + Environment.NewLine);
            }
            else if (path.EndsWith(".db", StringComparison.OrdinalIgnoreCase))
            {
                WriteSqliteFixture(path);
            }
            else if (path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            {
                File.WriteAllText(path, CsvFixture);
            }
            else if (path.Contains(".jsonl", StringComparison.OrdinalIgnoreCase))
            {
                File.WriteAllText(path, JsonLineFixture + Environment.NewLine);
            }
            else
            {
                File.WriteAllText(path, JsonFixture);
            }
        }

        public void WriteCodexSession(TokenUsageScanOptions options, string content)
        {
            TokenClientDefinition client = TokenClientCatalog.All.First(candidate => candidate.Id == "codex");
            string path = GetFixturePath(client, options);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        public void WriteCodexArchivedSession(TokenUsageScanOptions options, string content)
        {
            TokenClientDefinition client = TokenClientCatalog.All.First(candidate => candidate.Id == "codex");
            string path = Path.Combine(options.ResolveRoot(client), "archived_sessions", "archived.jsonl");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }

        private static void WriteSqliteFixture(string path)
        {
            SqliteConnectionStringBuilder builder = new()
            {
                DataSource = path,
                Pooling = false,
            };
            using SqliteConnection connection = new(builder.ToString());
            connection.Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE usage (
                    timestamp TEXT,
                    sessionId TEXT,
                    model TEXT,
                    provider TEXT,
                    input_tokens INTEGER,
                    output_tokens INTEGER,
                    cache_read_input_tokens INTEGER,
                    cache_creation_input_tokens INTEGER,
                    reasoning_output_tokens INTEGER,
                    workspace TEXT
                );
                INSERT INTO usage VALUES (
                    '2026-05-31T10:00:00+10:00',
                    'session-sqlite',
                    'gpt-5.3-codex',
                    'openai',
                    100,
                    20,
                    10,
                    5,
                    2,
                    'workspace'
                );
                """;
            command.ExecuteNonQuery();
        }

        private static string FixtureFileName(string pattern)
        {
            return pattern switch
            {
                "*.json" => "fixture.json",
                "*.jsonl" => "fixture.jsonl",
                "*.jsonl*" => "fixture.jsonl",
                "*.settings.json" => "fixture.settings.json",
                "usage*.csv" => "usage-fixture.csv",
                "T-*.json" => "T-fixture.json",
                _ when pattern.Contains('*', StringComparison.Ordinal) => pattern.Replace("*", "fixture", StringComparison.Ordinal),
                _ => pattern,
            };
        }

        private const string JsonFixture = """
            {
              "timestamp": "2026-05-31T10:00:00+10:00",
              "sessionId": "session-json",
              "model": "gpt-5.3-codex",
              "provider": "openai",
              "workspace": "workspace",
              "usage": {
                "input_tokens": 100,
                "output_tokens": 20,
                "cache_read_input_tokens": 10,
                "cache_creation_input_tokens": 5,
                "reasoning_output_tokens": 2
              }
            }
            """;

        private const string JsonLineFixture = """{"timestamp":"2026-05-31T10:00:00+10:00","sessionId":"session-jsonl","model":"gpt-5.3-codex","provider":"openai","workspace":"workspace","usage":{"input_tokens":100,"output_tokens":20,"cache_read_input_tokens":10,"cache_creation_input_tokens":5,"reasoning_output_tokens":2}}""";

        private const string CodexJsonLineFixture = """
            {"timestamp":"2026-05-31T10:00:00+10:00","type":"session_meta","payload":{"id":"session-jsonl","cwd":"workspace","model_provider":"openai"}}
            {"timestamp":"2026-05-31T10:00:01+10:00","type":"turn_context","payload":{"model":"gpt-5.3-codex","cwd":"workspace"}}
            {"timestamp":"2026-05-31T10:00:02+10:00","type":"event_msg","payload":{"type":"token_count","info":{"last_token_usage":{"input_tokens":100,"output_tokens":20,"cache_read_input_tokens":10,"cache_creation_input_tokens":5,"reasoning_output_tokens":2}}}}
            """;

        private const string CsvFixture = """
            timestamp,sessionId,model,provider,input_tokens,output_tokens,cache_read_input_tokens,cache_creation_input_tokens,reasoning_output_tokens,workspace
            2026-05-31T10:00:00+10:00,session-csv,gpt-5.3-codex,openai,100,20,10,5,2,workspace
            """;
    }
}
