namespace CodexUsageTray.Core.Services.TokenMeter;

public enum TokenClientRoot
{
    Home,
    XdgData,
    Config,
    EnvVar,
}

public sealed record TokenClientDefinition(
    string Id,
    TokenClientRoot Root,
    string RelativePath,
    string Pattern,
    string? EnvironmentVariable = null,
    string? FallbackRelativePath = null,
    IReadOnlyList<string>? AdditionalRelativePaths = null);
