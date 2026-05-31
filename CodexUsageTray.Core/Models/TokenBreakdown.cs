namespace CodexUsageTray.Core.Models;

public sealed record TokenBreakdown(
    long Input,
    long Output,
    long CacheRead,
    long CacheWrite,
    long Reasoning)
{
    public static TokenBreakdown Empty { get; } = new(0, 0, 0, 0, 0);

    public long Total => Input + Output + CacheRead + CacheWrite + Reasoning;

    public bool HasAnyTokens => Total > 0;

    public TokenBreakdown Add(TokenBreakdown other)
    {
        return new TokenBreakdown(
            Input + other.Input,
            Output + other.Output,
            CacheRead + other.CacheRead,
            CacheWrite + other.CacheWrite,
            Reasoning + other.Reasoning);
    }
}
