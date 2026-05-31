using System.Globalization;
using System.Text;
using System.Text.Json;
using CodexUsageTray.Core.Models;
using Microsoft.Data.Sqlite;

namespace CodexUsageTray.Core.Services.TokenMeter;

public sealed class TokenUsageFileParser
{
    private static readonly string[] UsageObjectNames =
    [
        "usage", "usage_metadata", "usageMetadata", "token_usage", "tokenUsage",
        "tokens", "last_token_usage", "lastTokenUsage", "total_token_usage", "totalTokenUsage",
        "usageMetadata"
    ];

    private static readonly string[] InputNames =
    [
        "input", "input_tokens", "inputTokens", "prompt_tokens", "promptTokens",
        "promptTokenCount", "prompt_token_count", "prompt", "tokens_input"
    ];

    private static readonly string[] OutputNames =
    [
        "output", "output_tokens", "outputTokens", "completion_tokens", "completionTokens",
        "candidatesTokenCount", "completion_token_count", "completion", "generated"
    ];

    private static readonly string[] CacheReadNames =
    [
        "cache_read", "cacheRead", "cache_read_input_tokens", "cacheReadInputTokens",
        "cached_input_tokens", "cachedInputTokens", "cachedContentTokenCount",
        "cached_tokens", "cache_read_tokens"
    ];

    private static readonly string[] CacheWriteNames =
    [
        "cache_write", "cacheWrite", "cache_creation_input_tokens", "cacheCreationInputTokens",
        "cache_write_input_tokens", "cacheWriteInputTokens", "cache_creation_tokens"
    ];

    private static readonly string[] ReasoningNames =
    [
        "reasoning", "reasoning_tokens", "reasoningTokens", "reasoning_output_tokens",
        "reasoningOutputTokens", "thoughtsTokenCount", "thinking_tokens"
    ];

    private static readonly string[] ModelNames =
    [
        "model", "model_id", "modelId", "model_name", "modelName", "model_slug", "slug"
    ];

    private static readonly string[] ProviderNames =
    [
        "provider", "provider_id", "providerId", "model_provider", "modelProvider", "source"
    ];

    private static readonly string[] TimestampNames =
    [
        "timestamp", "created_at", "createdAt", "updated_at", "updatedAt", "time",
        "datetime", "date", "ts", "start_time", "startTime"
    ];

    private static readonly string[] SessionNames =
    [
        "session_id", "sessionId", "session", "conversation_id", "conversationId", "thread_id", "threadId", "id"
    ];

    private static readonly string[] WorkspaceNames =
    [
        "workspace_key", "workspaceKey", "workspace", "cwd", "project_path", "projectPath", "repo", "root"
    ];

    public TokenFileParseResult ParseFile(TokenClientDefinition client, string path)
    {
        try
        {
            if (string.Equals(client.Id, "codex", StringComparison.OrdinalIgnoreCase))
            {
                return new TokenFileParseResult(ParseCodexJsonLinesFile(client, path), true);
            }

            string extension = Path.GetExtension(path);
            IReadOnlyList<TokenUsageEntry> entries;
            if (string.Equals(extension, ".csv", StringComparison.OrdinalIgnoreCase))
            {
                entries = ParseCsvFile(client, path);
            }
            else if (string.Equals(extension, ".db", StringComparison.OrdinalIgnoreCase)
                || string.Equals(Path.GetFileName(path), "state.db", StringComparison.OrdinalIgnoreCase)
                || string.Equals(Path.GetFileName(path), "sessions.db", StringComparison.OrdinalIgnoreCase)
                || string.Equals(Path.GetFileName(path), "kilo.db", StringComparison.OrdinalIgnoreCase))
            {
                entries = ParseSqliteFile(client, path);
            }
            else if (IsJsonLines(path))
            {
                entries = ParseJsonLinesFile(client, path);
            }
            else
            {
                entries = ParseJsonFile(client, path);
            }

            return new TokenFileParseResult(entries, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or SqliteException or FormatException)
        {
            return new TokenFileParseResult(Array.Empty<TokenUsageEntry>(), false, ex.Message);
        }
    }

    private static bool IsJsonLines(string path)
    {
        string fileName = Path.GetFileName(path);
        return fileName.Contains(".jsonl", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Path.GetExtension(path), ".jsonl", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<TokenUsageEntry> ParseCodexJsonLinesFile(TokenClientDefinition client, string path)
    {
        List<TokenUsageEntry> entries = [];
        string sessionId = Path.GetFileNameWithoutExtension(path);
        string model = "unknown";
        string provider = string.Empty;
        string? workspace = null;
        CodexTotals? previousTotals = null;

        foreach (string line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                using JsonDocument document = JsonDocument.Parse(line);
                JsonElement root = document.RootElement;
                string? rootType = FindStringDeep(root, ["type"], maxDepth: 0);
                DateTimeOffset timestamp = FindTimestamp(root, path);

                if (!TryGetProperty(root, "payload", out JsonElement payload) || payload.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                sessionId = FindStringDeep(payload, SessionNames, maxDepth: 1) ?? sessionId;
                model = FindStringDeep(payload, ModelNames, maxDepth: 3) ?? model;
                provider = FindStringDeep(payload, ProviderNames, maxDepth: 2) ?? provider;
                workspace = FindStringDeep(payload, WorkspaceNames, maxDepth: 2) ?? workspace;

                string? payloadType = FindStringDeep(payload, ["type"], maxDepth: 0);
                if (!string.Equals(rootType, "event_msg", StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(payloadType, "token_count", StringComparison.OrdinalIgnoreCase)
                    || !TryGetProperty(payload, "info", out JsonElement info)
                    || info.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                CodexTotals? total = TryGetProperty(info, "total_token_usage", out JsonElement totalUsage) && totalUsage.ValueKind == JsonValueKind.Object
                    ? ExtractCodexTotals(totalUsage)
                    : null;
                CodexTotals? last = TryGetProperty(info, "last_token_usage", out JsonElement lastUsage) && lastUsage.ValueKind == JsonValueKind.Object
                    ? ExtractCodexTotals(lastUsage)
                    : null;

                (CodexTotals? tokenTotals, CodexTotals? nextTotals) = ResolveCodexTokenIncrement(total, last, previousTotals);
                if (tokenTotals is null)
                {
                    if (total is not null && last is null && previousTotals is not null && total.Value.DeltaFrom(previousTotals.Value) is null)
                    {
                        previousTotals = total;
                    }

                    continue;
                }

                TokenBreakdown tokens = tokenTotals.Value.IntoTokens();
                if (!tokens.HasAnyTokens)
                {
                    continue;
                }

                previousTotals = nextTotals;

                entries.Add(new TokenUsageEntry(
                    client.Id,
                    model,
                    provider,
                    sessionId,
                    workspace,
                    workspace,
                    timestamp,
                    tokens,
                    1,
                    FindStringDeep(payload, ["agent", "agent_name", "agentName", "agent_nickname"], maxDepth: 2),
                    null,
                    null,
                    path));
            }
            catch (JsonException)
            {
                // One corrupt Codex log row should not hide usage from the rest of the session.
            }
        }

        return entries;
    }

    private static IReadOnlyList<TokenUsageEntry> ParseJsonLinesFile(TokenClientDefinition client, string path)
    {
        List<TokenUsageEntry> entries = [];
        int lineNumber = 0;
        foreach (string line in File.ReadLines(path))
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                using JsonDocument document = JsonDocument.Parse(line);
                ExtractEntries(document.RootElement, client, path, $"{Path.GetFileNameWithoutExtension(path)}:{lineNumber}", entries);
            }
            catch (JsonException)
            {
                // One corrupt log row should not hide usage from the rest of the file.
            }
        }

        return entries;
    }

    private static IReadOnlyList<TokenUsageEntry> ParseJsonFile(TokenClientDefinition client, string path)
    {
        using FileStream stream = File.OpenRead(path);
        using JsonDocument document = JsonDocument.Parse(stream, new JsonDocumentOptions { AllowTrailingCommas = true });
        List<TokenUsageEntry> entries = [];
        ExtractEntries(document.RootElement, client, path, Path.GetFileNameWithoutExtension(path), entries);
        return entries;
    }

    private static IReadOnlyList<TokenUsageEntry> ParseCsvFile(TokenClientDefinition client, string path)
    {
        List<TokenUsageEntry> entries = [];
        using StreamReader reader = File.OpenText(path);
        string? headerLine = reader.ReadLine();
        if (string.IsNullOrWhiteSpace(headerLine))
        {
            return entries;
        }

        string[] headers = SplitCsvLine(headerLine).Select(NormalizeName).ToArray();
        int rowNumber = 1;
        while (!reader.EndOfStream)
        {
            string? line = reader.ReadLine();
            rowNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            string[] values = SplitCsvLine(line);
            Dictionary<string, string> row = new(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < headers.Length && i < values.Length; i++)
            {
                row[headers[i]] = values[i];
            }

            TokenBreakdown tokens = new(
                FindLong(row, InputNames),
                FindLong(row, OutputNames),
                FindLong(row, CacheReadNames),
                FindLong(row, CacheWriteNames),
                FindLong(row, ReasoningNames));
            if (!tokens.HasAnyTokens)
            {
                continue;
            }

            entries.Add(new TokenUsageEntry(
                client.Id,
                FindString(row, ModelNames) ?? "unknown",
                FindString(row, ProviderNames) ?? string.Empty,
                FindString(row, SessionNames) ?? $"{Path.GetFileNameWithoutExtension(path)}:{rowNumber}",
                FindString(row, WorkspaceNames),
                FindString(row, WorkspaceNames),
                FindTimestamp(row, path),
                tokens,
                (int)Math.Max(1, FindLong(row, ["message_count", "messageCount", "messages"])),
                FindString(row, ["agent", "agent_name", "agentName"]),
                FindDecimal(row, ["cost", "price", "amount"]),
                null,
                path));
        }

        return entries;
    }

    private static IReadOnlyList<TokenUsageEntry> ParseSqliteFile(TokenClientDefinition client, string path)
    {
        List<TokenUsageEntry> entries = [];
        SqliteConnectionStringBuilder builder = new()
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        };

        using SqliteConnection connection = new(builder.ToString());
        connection.Open();

        List<string> tables = [];
        using (SqliteCommand tableCommand = connection.CreateCommand())
        {
            tableCommand.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'";
            using SqliteDataReader reader = tableCommand.ExecuteReader();
            while (reader.Read())
            {
                tables.Add(reader.GetString(0));
            }
        }

        foreach (string table in tables)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = $"SELECT * FROM \"{table.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                Dictionary<string, string> row = new(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    if (reader.IsDBNull(i))
                    {
                        continue;
                    }

                    row[NormalizeName(reader.GetName(i))] = Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture) ?? string.Empty;
                }

                entries.AddRange(ParseStructuredRow(client, path, $"{table}:{entries.Count + 1}", row));
            }
        }

        return entries;
    }

    private static IEnumerable<TokenUsageEntry> ParseStructuredRow(TokenClientDefinition client, string path, string fallbackSessionId, IReadOnlyDictionary<string, string> row)
    {
        TokenBreakdown tokens = new(
            FindLong(row, InputNames),
            FindLong(row, OutputNames),
            FindLong(row, CacheReadNames),
            FindLong(row, CacheWriteNames),
            FindLong(row, ReasoningNames));

        decimal? existingCost = FindDecimal(row, ["cost", "price", "amount"]);
        if (!tokens.HasAnyTokens && existingCost is null)
        {
            foreach (string value in row.Values)
            {
                if (LooksLikeJson(value))
                {
                    using JsonDocument nested = JsonDocument.Parse(value);
                    List<TokenUsageEntry> nestedEntries = [];
                    ExtractEntries(nested.RootElement, client, path, fallbackSessionId, nestedEntries);
                    foreach (TokenUsageEntry entry in nestedEntries)
                    {
                        yield return entry;
                    }
                }
            }

            yield break;
        }

        if (!tokens.HasAnyTokens && existingCost is not null)
        {
            tokens = TokenBreakdown.Empty;
        }

        if (tokens.HasAnyTokens || existingCost is not null)
        {
            yield return new TokenUsageEntry(
                client.Id,
                FindString(row, ModelNames) ?? "unknown",
                FindString(row, ProviderNames) ?? string.Empty,
                FindString(row, SessionNames) ?? fallbackSessionId,
                FindString(row, WorkspaceNames),
                FindString(row, WorkspaceNames),
                FindTimestamp(row, path),
                tokens,
                (int)Math.Max(1, FindLong(row, ["message_count", "messageCount", "messages"])),
                FindString(row, ["agent", "agent_name", "agentName"]),
                existingCost,
                existingCost is null ? null : "source",
                path);
        }
    }

    private static void ExtractEntries(JsonElement element, TokenClientDefinition client, string path, string fallbackSessionId, List<TokenUsageEntry> entries)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (TryCreateEntry(element, client, path, fallbackSessionId, out TokenUsageEntry? entry))
                {
                    entries.Add(entry!);
                    return;
                }

                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (ShouldRecurseProperty(property.Name, property.Value))
                    {
                        ExtractEntries(property.Value, client, path, fallbackSessionId, entries);
                    }
                }

                break;
            case JsonValueKind.Array:
                foreach (JsonElement item in element.EnumerateArray())
                {
                    ExtractEntries(item, client, path, fallbackSessionId, entries);
                }

                break;
        }
    }

    private static bool ShouldRecurseProperty(string propertyName, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object && value.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        string normalized = NormalizeName(propertyName);
        return string.Equals(normalized, "messages", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "entries", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "items", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "sessions", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "records", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "turns", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "events", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "requests", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "responses", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "data", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "children", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryCreateEntry(JsonElement element, TokenClientDefinition client, string path, string fallbackSessionId, out TokenUsageEntry? entry)
    {
        entry = null;
        JsonElement usage = FindUsageElement(element) ?? element;
        TokenBreakdown tokens = ExtractTokens(usage);
        decimal? existingCost = FindDecimal(element, ["cost", "price", "amount", "total_cost", "totalCost"]);
        if (!tokens.HasAnyTokens && existingCost is null)
        {
            return false;
        }

        string model = FindStringDeep(element, ModelNames) ?? "unknown";
        string provider = FindStringDeep(element, ProviderNames) ?? string.Empty;
        string sessionId = FindStringDeep(element, SessionNames) ?? fallbackSessionId;
        string? workspace = FindStringDeep(element, WorkspaceNames);
        DateTimeOffset timestamp = FindTimestamp(element, path);

        entry = new TokenUsageEntry(
            client.Id,
            model,
            provider,
            sessionId,
            workspace,
            workspace,
            timestamp,
            tokens,
            (int)Math.Max(1, FindLongDeep(element, ["message_count", "messageCount", "messages", "turns"]) ?? 1),
            FindStringDeep(element, ["agent", "agent_name", "agentName", "agent_nickname"]),
            existingCost,
            existingCost is null ? null : "source",
            path);
        return true;
    }

    private static JsonElement? FindUsageElement(JsonElement element)
    {
        foreach (string name in UsageObjectNames)
        {
            if (TryGetProperty(element, name, out JsonElement usage) && usage.ValueKind == JsonValueKind.Object)
            {
                return usage;
            }
        }

        if (TryGetProperty(element, "payload", out JsonElement payload) && payload.ValueKind == JsonValueKind.Object)
        {
            if (TryGetProperty(payload, "info", out JsonElement info) && info.ValueKind == JsonValueKind.Object)
            {
                foreach (string name in UsageObjectNames)
                {
                    if (TryGetProperty(info, name, out JsonElement usage) && usage.ValueKind == JsonValueKind.Object)
                    {
                        return usage;
                    }
                }
            }
        }

        return null;
    }

    private static TokenBreakdown ExtractTokens(JsonElement usage)
    {
        long input = FindLongDeep(usage, InputNames) ?? 0;
        long cacheRead = FindLongDeep(usage, CacheReadNames) ?? 0;
        if (cacheRead > input && input > 0)
        {
            cacheRead = input;
        }

        return new TokenBreakdown(
            Math.Max(0, input - Math.Max(0, cacheRead)),
            Math.Max(0, FindLongDeep(usage, OutputNames) ?? 0),
            Math.Max(0, cacheRead),
            Math.Max(0, FindLongDeep(usage, CacheWriteNames) ?? 0),
            Math.Max(0, FindLongDeep(usage, ReasoningNames) ?? 0));
    }

    private static CodexTotals? ExtractCodexTotals(JsonElement usage)
    {
        long input = FindLongDeep(usage, ["input_tokens", "inputTokens"], maxDepth: 1) ?? 0;
        long cachedInput = FindLongDeep(usage, ["cached_input_tokens", "cachedInputTokens"], maxDepth: 1) ?? 0;
        long cacheReadInput = FindLongDeep(usage, ["cache_read_input_tokens", "cacheReadInputTokens"], maxDepth: 1) ?? 0;
        long cacheRead = Math.Max(cachedInput, cacheReadInput);

        return new CodexTotals(
            Math.Max(0, input),
            Math.Max(0, FindLongDeep(usage, ["output_tokens", "outputTokens"], maxDepth: 1) ?? 0),
            Math.Max(0, cacheRead),
            Math.Max(0, FindLongDeep(usage, ["reasoning_output_tokens", "reasoningOutputTokens", "reasoning_tokens", "reasoningTokens"], maxDepth: 1) ?? 0));
    }

    private static (CodexTotals? TokenTotals, CodexTotals? NextTotals) ResolveCodexTokenIncrement(CodexTotals? total, CodexTotals? last, CodexTotals? previous)
    {
        if (total is not null && last is not null && previous is not null)
        {
            if (total.Value == previous.Value)
            {
                return (null, previous);
            }

            if (total.Value.DeltaFrom(previous.Value) is null && total.Value.LooksLikeStaleRegression(previous.Value, last.Value))
            {
                return (null, previous);
            }

            return (last, total);
        }

        if (total is not null && last is not null)
        {
            return (last, total);
        }

        if (total is not null && previous is not null)
        {
            if (total.Value == previous.Value)
            {
                return (null, previous);
            }

            CodexTotals? delta = total.Value.DeltaFrom(previous.Value);
            return delta is null ? (null, total) : (delta, total);
        }

        if (total is not null)
        {
            return (total, total);
        }

        if (last is not null && previous is not null)
        {
            return (last, previous.Value.SaturatingAdd(last.Value));
        }

        if (last is not null)
        {
            return (last, null);
        }

        return (null, previous);
    }

    private static string[] SplitCsvLine(string line)
    {
        List<string> values = [];
        StringBuilder current = new();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char ch = line[i];
            if (ch == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (ch == ',' && !inQuotes)
            {
                values.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }

        values.Add(current.ToString());
        return values.ToArray();
    }

    private static string NormalizeName(string value)
    {
        return value.Trim().Replace("-", "_", StringComparison.Ordinal).Replace(" ", "_", StringComparison.Ordinal);
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            value = default;
            return false;
        }

        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)
                || string.Equals(NormalizeName(property.Name), NormalizeName(name), StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string? FindStringDeep(JsonElement element, IEnumerable<string> names, int depth = 0, int maxDepth = 4)
    {
        if (depth > maxDepth)
        {
            return null;
        }

        foreach (string name in names)
        {
            if (TryGetProperty(element, name, out JsonElement value))
            {
                string? text = ElementToString(value);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Object)
            {
                string? nested = FindStringDeep(property.Value, names, depth + 1, maxDepth);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }

        return null;
    }

    private static long? FindLongDeep(JsonElement element, IEnumerable<string> names, int depth = 0, int maxDepth = 4)
    {
        if (depth > maxDepth)
        {
            return null;
        }

        foreach (string name in names)
        {
            if (TryGetProperty(element, name, out JsonElement value) && TryGetLong(value, out long result))
            {
                return result;
            }
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Object)
            {
                long? nested = FindLongDeep(property.Value, names, depth + 1, maxDepth);
                if (nested is not null)
                {
                    return nested;
                }
            }
        }

        return null;
    }

    private static decimal? FindDecimal(JsonElement element, IEnumerable<string> names)
    {
        foreach (string name in names)
        {
            if (TryGetProperty(element, name, out JsonElement value))
            {
                if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out decimal decimalValue))
                {
                    return decimalValue;
                }

                if (value.ValueKind == JsonValueKind.String
                    && decimal.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out decimal parsed))
                {
                    return parsed;
                }
            }
        }

        return null;
    }

    private static DateTimeOffset FindTimestamp(JsonElement element, string path)
    {
        foreach (string name in TimestampNames)
        {
            if (TryGetProperty(element, name, out JsonElement value) && TryGetTimestamp(value, out DateTimeOffset timestamp))
            {
                return timestamp;
            }
        }

        return File.Exists(path) ? new FileInfo(path).LastWriteTime : DateTimeOffset.Now;
    }

    private static DateTimeOffset FindTimestamp(IReadOnlyDictionary<string, string> row, string path)
    {
        foreach (string name in TimestampNames.Select(NormalizeName))
        {
            if (row.TryGetValue(name, out string? value) && TryGetTimestamp(value, out DateTimeOffset timestamp))
            {
                return timestamp;
            }
        }

        return File.Exists(path) ? new FileInfo(path).LastWriteTime : DateTimeOffset.Now;
    }

    private static string? FindString(IReadOnlyDictionary<string, string> row, IEnumerable<string> names)
    {
        foreach (string name in names.Select(NormalizeName))
        {
            if (row.TryGetValue(name, out string? value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static long FindLong(IReadOnlyDictionary<string, string> row, IEnumerable<string> names)
    {
        foreach (string name in names.Select(NormalizeName))
        {
            if (row.TryGetValue(name, out string? value)
                && long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long result))
            {
                return Math.Max(0, result);
            }
        }

        return 0;
    }

    private static decimal? FindDecimal(IReadOnlyDictionary<string, string> row, IEnumerable<string> names)
    {
        foreach (string name in names.Select(NormalizeName))
        {
            if (row.TryGetValue(name, out string? value)
                && decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal result))
            {
                return result;
            }
        }

        return null;
    }

    private static bool TryGetLong(JsonElement element, out long value)
    {
        value = 0;
        return element.ValueKind switch
        {
            JsonValueKind.Number => element.TryGetInt64(out value),
            JsonValueKind.String => long.TryParse(element.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value),
            _ => false,
        };
    }

    private static string? ElementToString(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.ToString(),
            _ => null,
        };
    }

    private static bool TryGetTimestamp(JsonElement element, out DateTimeOffset timestamp)
    {
        timestamp = default;
        if (element.ValueKind == JsonValueKind.String)
        {
            return TryGetTimestamp(element.GetString(), out timestamp);
        }

        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out long numeric))
        {
            timestamp = NumericTimestampToDateTimeOffset(numeric);
            return true;
        }

        return false;
    }

    private static bool TryGetTimestamp(string? value, out DateTimeOffset timestamp)
    {
        timestamp = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out timestamp))
        {
            return true;
        }

        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long numeric))
        {
            timestamp = NumericTimestampToDateTimeOffset(numeric);
            return true;
        }

        return false;
    }

    private static DateTimeOffset NumericTimestampToDateTimeOffset(long value)
    {
        if (value > 10_000_000_000)
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(value);
        }

        return DateTimeOffset.FromUnixTimeSeconds(value);
    }

    private static bool LooksLikeJson(string value)
    {
        string trimmed = value.TrimStart();
        return trimmed.StartsWith('{') || trimmed.StartsWith('[');
    }

    private readonly record struct CodexTotals(long Input, long Output, long CacheRead, long Reasoning)
    {
        public long Total => Input + Output + CacheRead + Reasoning;

        public CodexTotals? DeltaFrom(CodexTotals previous)
        {
            if (Input < previous.Input
                || Output < previous.Output
                || CacheRead < previous.CacheRead
                || Reasoning < previous.Reasoning)
            {
                return null;
            }

            return new CodexTotals(
                Input - previous.Input,
                Output - previous.Output,
                CacheRead - previous.CacheRead,
                Reasoning - previous.Reasoning);
        }

        public CodexTotals SaturatingAdd(CodexTotals other)
        {
            return new CodexTotals(
                Input + other.Input,
                Output + other.Output,
                CacheRead + other.CacheRead,
                Reasoning + other.Reasoning);
        }

        public bool LooksLikeStaleRegression(CodexTotals previous, CodexTotals last)
        {
            long previousTotal = previous.Total;
            long currentTotal = Total;
            long lastTotal = last.Total;
            if (previousTotal <= 0 || currentTotal <= 0 || lastTotal <= 0)
            {
                return false;
            }

            return currentTotal * 100 >= previousTotal * 98
                || currentTotal + (lastTotal * 2) >= previousTotal;
        }

        public TokenBreakdown IntoTokens()
        {
            long clampedCacheRead = Math.Min(CacheRead, Input);
            return new TokenBreakdown(
                Math.Max(0, Input - clampedCacheRead),
                Math.Max(0, Output),
                Math.Max(0, clampedCacheRead),
                0,
                Math.Max(0, Reasoning));
        }
    }
}
