using CodexUsageTray.Core.Models;

namespace CodexUsageTray.Core.Services.TokenMeter;

public sealed class TokenPricingDataset
{
    private readonly Dictionary<string, TokenModelPricing> _litellm;
    private readonly Dictionary<string, TokenModelPricing> _openRouter;
    private readonly Dictionary<string, TokenModelPricing> _cursorOverrides;

    public TokenPricingDataset(
        Dictionary<string, TokenModelPricing>? litellm,
        Dictionary<string, TokenModelPricing>? openRouter,
        DateTimeOffset capturedAt,
        bool usedStaleCache,
        string status)
    {
        _litellm = FilterLiteLlm(litellm ?? []);
        _openRouter = openRouter ?? [];
        _cursorOverrides = BuildCursorOverrides();
        Snapshot = new PricingSnapshot(capturedAt, usedStaleCache, _litellm.Count, _openRouter.Count, status);
    }

    public PricingSnapshot Snapshot { get; }

    public TokenCostEstimate? Calculate(string model, string provider, TokenBreakdown tokens)
    {
        if (!tokens.HasAnyTokens || string.IsNullOrWhiteSpace(model) || string.Equals(model, "unknown", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        ResolvedPricing? resolved = Resolve(model, provider);
        if (resolved is null)
        {
            return null;
        }

        decimal cost = CalculateCost(tokens, resolved.Pricing);
        return new TokenCostEstimate(cost, resolved.Source, resolved.MatchedKey);
    }

    private ResolvedPricing? Resolve(string model, string provider)
    {
        List<string> candidates = BuildLookupCandidates(model, provider);
        foreach (string candidate in candidates)
        {
            if (TryResolveExact(_litellm, candidate, "LiteLLM", out ResolvedPricing? litellm))
            {
                return litellm;
            }

            if (TryResolveExact(_openRouter, candidate, "OpenRouter", out ResolvedPricing? openRouter))
            {
                return openRouter;
            }

            if (TryResolveExact(_cursorOverrides, candidate, "Cursor override", out ResolvedPricing? cursor))
            {
                return cursor;
            }
        }

        foreach (string candidate in candidates.Select(NormalizeModelId).Where(static value => value.Length >= 5).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            ResolvedPricing? fuzzy = TryResolveContains(_litellm, candidate, "LiteLLM")
                ?? TryResolveContains(_openRouter, candidate, "OpenRouter")
                ?? TryResolveContains(_cursorOverrides, candidate, "Cursor override");
            if (fuzzy is not null)
            {
                return fuzzy;
            }
        }

        return null;
    }

    private static bool TryResolveExact(Dictionary<string, TokenModelPricing> values, string key, string source, out ResolvedPricing? resolved)
    {
        resolved = null;
        foreach ((string candidate, TokenModelPricing pricing) in values)
        {
            if (string.Equals(candidate, key, StringComparison.OrdinalIgnoreCase)
                || string.Equals(candidate.Split('/').Last(), key, StringComparison.OrdinalIgnoreCase))
            {
                resolved = new ResolvedPricing(pricing, source, candidate);
                return true;
            }
        }

        return false;
    }

    private static ResolvedPricing? TryResolveContains(Dictionary<string, TokenModelPricing> values, string key, string source)
    {
        foreach ((string candidate, TokenModelPricing pricing) in values.OrderByDescending(pair => pair.Key.Length))
        {
            string normalizedCandidate = NormalizeModelId(candidate.Split('/').Last());
            if (normalizedCandidate.Length >= 5
                && (normalizedCandidate.Contains(key, StringComparison.OrdinalIgnoreCase)
                    || key.Contains(normalizedCandidate, StringComparison.OrdinalIgnoreCase)))
            {
                return new ResolvedPricing(pricing, source, candidate);
            }
        }

        return null;
    }

    private static List<string> BuildLookupCandidates(string model, string provider)
    {
        List<string> candidates = [];
        Add(model);

        if (!string.IsNullOrWhiteSpace(provider) && !model.Contains('/', StringComparison.Ordinal))
        {
            Add($"{provider.Trim().ToLowerInvariant()}/{model}");
        }

        Add(NormalizeModelId(model));

        if (model.Contains("(", StringComparison.Ordinal) && model.EndsWith(")", StringComparison.Ordinal))
        {
            int index = model.LastIndexOf('(');
            if (index > 0)
            {
                Add(model[..index].Trim());
            }
        }

        string withoutDateSuffix = StripDateSuffix(model);
        Add(withoutDateSuffix);
        return candidates;

        void Add(string value)
        {
            if (!string.IsNullOrWhiteSpace(value) && !candidates.Contains(value, StringComparer.OrdinalIgnoreCase))
            {
                candidates.Add(value);
            }
        }
    }

    private static string NormalizeModelId(string model)
    {
        string value = model.Trim().ToLowerInvariant();
        value = value.Replace('_', '-');
        value = value.Replace(' ', '-');
        return StripDateSuffix(value);
    }

    private static string StripDateSuffix(string value)
    {
        string trimmed = value.Trim();
        if (trimmed.Length > 9 && trimmed[^9] == '-' && trimmed[^8..].All(char.IsAsciiDigit))
        {
            return trimmed[..^9];
        }

        return trimmed;
    }

    private static decimal CalculateCost(TokenBreakdown tokens, TokenModelPricing pricing)
    {
        return CostWithTiers(tokens.Input, pricing.InputCostPerToken, [
                new(128_000, pricing.InputCostPerTokenAbove128KTokens),
                new(200_000, pricing.InputCostPerTokenAbove200KTokens),
                new(256_000, pricing.InputCostPerTokenAbove256KTokens),
                new(272_000, pricing.InputCostPerTokenAbove272KTokens),
            ])
            + CostWithTiers(tokens.Output + tokens.Reasoning, pricing.OutputCostPerToken, [
                new(128_000, pricing.OutputCostPerTokenAbove128KTokens),
                new(200_000, pricing.OutputCostPerTokenAbove200KTokens),
                new(256_000, pricing.OutputCostPerTokenAbove256KTokens),
                new(272_000, pricing.OutputCostPerTokenAbove272KTokens),
            ])
            + CostWithTiers(tokens.CacheRead, pricing.CacheReadInputTokenCost, [
                new(200_000, pricing.CacheReadInputTokenCostAbove200KTokens),
                new(272_000, pricing.CacheReadInputTokenCostAbove272KTokens),
            ])
            + CostWithTiers(tokens.CacheWrite, pricing.CacheCreationInputTokenCost, [
                new(200_000, pricing.CacheCreationInputTokenCostAbove200KTokens),
            ]);
    }

    private static decimal CostWithTiers(long tokens, decimal? baseRate, IReadOnlyList<PricingTier> tiers)
    {
        if (tokens <= 0 || baseRate is null)
        {
            return 0;
        }

        decimal total = 0;
        long consumed = 0;
        decimal currentRate = baseRate.Value;

        foreach (PricingTier tier in tiers.OrderBy(static tier => tier.ThresholdTokens))
        {
            if (tier.Rate is null || tier.ThresholdTokens <= consumed)
            {
                continue;
            }

            long tierTokens = Math.Min(tokens, tier.ThresholdTokens) - consumed;
            if (tierTokens > 0)
            {
                total += tierTokens * currentRate;
                consumed += tierTokens;
            }

            if (tokens <= consumed)
            {
                return total;
            }

            currentRate = tier.Rate.Value;
        }

        total += (tokens - consumed) * currentRate;
        return total;
    }

    private static Dictionary<string, TokenModelPricing> FilterLiteLlm(Dictionary<string, TokenModelPricing> values)
    {
        Dictionary<string, TokenModelPricing> filtered = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string key, TokenModelPricing pricing) in values)
        {
            if (key.StartsWith("github_copilot/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            filtered.TryAdd(key, pricing);
        }

        return filtered;
    }

    private static Dictionary<string, TokenModelPricing> BuildCursorOverrides()
    {
        Dictionary<string, TokenModelPricing> values = new(StringComparer.OrdinalIgnoreCase);
        Add("gpt-5.3", 0.00000175m, 0.000014m, 0.000000175m);
        Add("gpt-5.3-codex", 0.00000175m, 0.000014m, 0.000000175m);
        Add("gpt-5.3-codex-spark", 0.00000175m, 0.000014m, 0.000000175m);
        Add("composer-1", 0.00000125m, 0.00001m, 0.000000125m);
        Add("composer-1.5", 0.0000035m, 0.0000175m, 0.00000035m);
        Add("composer-2", 0.0000005m, 0.0000025m, 0.0000002m);
        Add("composer-2-fast", 0.0000015m, 0.0000075m, 0.00000035m);
        return values;

        void Add(string key, decimal input, decimal output, decimal cacheRead)
        {
            values[key] = new TokenModelPricing
            {
                InputCostPerToken = input,
                OutputCostPerToken = output,
                CacheReadInputTokenCost = cacheRead,
            };
        }
    }

    private sealed record ResolvedPricing(TokenModelPricing Pricing, string Source, string MatchedKey);

    private sealed record PricingTier(long ThresholdTokens, decimal? Rate);
}
