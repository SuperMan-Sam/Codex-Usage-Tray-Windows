namespace CodexUsageTray.Core.Services.TokenMeter;

public static class TokenClientCatalog
{
    public static IReadOnlyList<TokenClientDefinition> All { get; } =
    [
        new("opencode", TokenClientRoot.XdgData, Path.Combine("opencode", "storage", "message"), "*.json"),
        new("claude", TokenClientRoot.Home, Path.Combine(".claude", "projects"), "*.jsonl"),
        new("codex", TokenClientRoot.EnvVar, "sessions", "*.jsonl", "CODEX_HOME", ".codex", ["archived_sessions"]),
        new("cursor", TokenClientRoot.Home, Path.Combine(".config", "tokscale", "cursor-cache"), "usage*.csv"),
        new("gemini", TokenClientRoot.Home, Path.Combine(".gemini", "tmp"), "*.json"),
        new("amp", TokenClientRoot.XdgData, Path.Combine("amp", "threads"), "T-*.json"),
        new("droid", TokenClientRoot.Home, Path.Combine(".factory", "sessions"), "*.settings.json"),
        new("openclaw", TokenClientRoot.Home, Path.Combine(".openclaw", "agents"), "*.jsonl*"),
        new("pi", TokenClientRoot.Home, Path.Combine(".pi", "agent", "sessions"), "*.jsonl"),
        new("kimi", TokenClientRoot.Home, Path.Combine(".kimi", "sessions"), "wire.jsonl"),
        new("qwen", TokenClientRoot.Home, Path.Combine(".qwen", "projects"), "*.jsonl"),
        new("roocode", TokenClientRoot.Home, Path.Combine(".config", "Code", "User", "globalStorage", "rooveterinaryinc.roo-cline", "tasks"), "ui_messages.json"),
        new("kilocode", TokenClientRoot.Home, Path.Combine(".config", "Code", "User", "globalStorage", "kilocode.kilo-code", "tasks"), "ui_messages.json"),
        new("mux", TokenClientRoot.Home, Path.Combine(".mux", "sessions"), "session-usage.json"),
        new("kilo", TokenClientRoot.XdgData, Path.Combine("kilo", "kilo.db"), "kilo.db"),
        new("crush", TokenClientRoot.XdgData, Path.Combine("crush", "projects.json"), "projects.json"),
        new("hermes", TokenClientRoot.EnvVar, "state.db", "state.db", "HERMES_HOME", ".hermes"),
        new("copilot", TokenClientRoot.Home, Path.Combine(".copilot", "otel"), "*.jsonl"),
        new("goose", TokenClientRoot.XdgData, Path.Combine("goose", "sessions", "sessions.db"), "sessions.db"),
        new("codebuff", TokenClientRoot.EnvVar, "projects", "chat-messages.json", "CODEBUFF_DATA_DIR", Path.Combine(".config", "manicode")),
        new("antigravity", TokenClientRoot.Config, Path.Combine("antigravity-cache", "sessions"), "*.jsonl"),
    ];
}
