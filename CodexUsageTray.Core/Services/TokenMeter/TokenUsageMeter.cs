using CodexUsageTray.Core.Models;

namespace CodexUsageTray.Core.Services.TokenMeter;

public sealed class TokenUsageMeter
{
    private readonly TokenUsageScanner _scanner;
    private readonly TokenPricingService _pricingService;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    public TokenUsageMeter()
        : this(new TokenUsageScanner(), new TokenPricingService())
    {
    }

    public TokenUsageMeter(TokenUsageScanner scanner, TokenPricingService pricingService)
    {
        _scanner = scanner;
        _pricingService = pricingService;
    }

    public async Task<TokenUsageSnapshot> RefreshAsync(
        TokenUsageRange range,
        string cacheDirectory,
        TokenUsageWindow? fiveHourWindow = null,
        TokenUsageScanOptions? scanOptions = null,
        CancellationToken cancellationToken = default)
    {
        if (!await _refreshGate.WaitAsync(0, cancellationToken))
        {
            return TokenUsageSnapshot.Empty(DateTimeOffset.Now, range, "Token refresh already in progress");
        }

        try
        {
            TokenUsageScanResult scanResult = await _scanner.ScanAsync(scanOptions, cancellationToken);
            TokenPricingDataset? pricing = null;
            try
            {
                pricing = await _pricingService.GetPricingAsync(cacheDirectory, cancellationToken);
            }
            catch (Exception ex) when (ex is IOException
                or UnauthorizedAccessException
                or System.Text.Json.JsonException
                or HttpRequestException
                or InvalidOperationException
                or ArgumentException)
            {
                pricing = null;
            }

            List<TokenUsageEntry> pricedEntries = new(scanResult.Entries.Count);

            foreach (TokenUsageEntry entry in scanResult.Entries)
            {
                if (entry.Cost is not null || pricing is null)
                {
                    pricedEntries.Add(entry);
                    continue;
                }

                TokenCostEstimate? estimate = pricing.Calculate(entry.Model, entry.Provider, entry.Tokens);
                pricedEntries.Add(estimate is null
                    ? entry
                    : entry with { Cost = estimate.Cost, CostSource = estimate.Source });
            }

            return TokenUsageAggregator.BuildSnapshot(
                pricedEntries,
                range,
                DateTimeOffset.Now,
                pricing?.Snapshot,
                scanResult.ScannedFileCount,
                scanResult.FailedFileCount,
                fiveHourWindow);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or HttpRequestException or TaskCanceledException)
        {
            return TokenUsageSnapshot.Empty(DateTimeOffset.Now, range, $"Token usage unavailable: {ex.Message}");
        }
        finally
        {
            _refreshGate.Release();
        }
    }
}
