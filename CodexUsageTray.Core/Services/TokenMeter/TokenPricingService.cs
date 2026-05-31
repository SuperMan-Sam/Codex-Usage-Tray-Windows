using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodexUsageTray.Core.Services.TokenMeter;

public sealed class TokenPricingService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(1);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };

    private const string LiteLlmCacheFileName = "pricing-litellm.json";
    private const string OpenRouterCacheFileName = "pricing-openrouter.json";
    private const string LiteLlmUrl = "https://raw.githubusercontent.com/BerriAI/litellm/main/model_prices_and_context_window.json";
    private const string OpenRouterModelsUrl = "https://openrouter.ai/api/v1/models";

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private TokenPricingDataset? _cachedDataset;

    public TokenPricingService()
        : this(new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30),
        }, ownsHttpClient: true)
    {
    }

    public TokenPricingService(HttpClient httpClient, bool ownsHttpClient = false)
    {
        _httpClient = httpClient;
        _ownsHttpClient = ownsHttpClient;
    }

    public async Task<TokenPricingDataset> GetPricingAsync(string cacheDirectory, CancellationToken cancellationToken = default)
    {
        if (_cachedDataset is not null && DateTimeOffset.Now - _cachedDataset.Snapshot.CapturedAt < CacheTtl)
        {
            return _cachedDataset;
        }

        Directory.CreateDirectory(cacheDirectory);

        Dictionary<string, TokenModelPricing>? litellm = await TryLoadFreshCacheAsync<Dictionary<string, TokenModelPricing>>(cacheDirectory, LiteLlmCacheFileName, cancellationToken);
        Dictionary<string, TokenModelPricing>? openRouter = await TryLoadFreshCacheAsync<Dictionary<string, TokenModelPricing>>(cacheDirectory, OpenRouterCacheFileName, cancellationToken);
        bool usedStale = false;
        string status = "Pricing loaded";

        try
        {
            litellm ??= await FetchLiteLlmAsync(cancellationToken);
            await SaveCacheAsync(cacheDirectory, LiteLlmCacheFileName, litellm, cancellationToken);
        }
        catch (Exception) when (litellm is null)
        {
            litellm = await TryLoadAnyCacheAsync<Dictionary<string, TokenModelPricing>>(cacheDirectory, LiteLlmCacheFileName, cancellationToken);
            usedStale = litellm is not null;
            status = litellm is null ? "Pricing unavailable" : "Pricing loaded from stale cache";
        }

        try
        {
            openRouter ??= await FetchOpenRouterAsync(cancellationToken);
            await SaveCacheAsync(cacheDirectory, OpenRouterCacheFileName, openRouter, cancellationToken);
        }
        catch (Exception) when (openRouter is null)
        {
            openRouter = await TryLoadAnyCacheAsync<Dictionary<string, TokenModelPricing>>(cacheDirectory, OpenRouterCacheFileName, cancellationToken);
            usedStale = usedStale || openRouter is not null;
            status = openRouter is null && litellm is null ? "Pricing unavailable" : "Pricing loaded from stale cache";
        }

        _cachedDataset = new TokenPricingDataset(litellm, openRouter, DateTimeOffset.Now, usedStale, status);
        return _cachedDataset;
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private async Task<Dictionary<string, TokenModelPricing>> FetchLiteLlmAsync(CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _httpClient.GetAsync(LiteLlmUrl, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<Dictionary<string, TokenModelPricing>>(stream, JsonOptions, cancellationToken)
            ?? [];
    }

    private async Task<Dictionary<string, TokenModelPricing>> FetchOpenRouterAsync(CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, OpenRouterModelsUrl);
        request.Headers.TryAddWithoutValidation("Content-Type", "application/json");
        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        OpenRouterModelsResponse? decoded = await JsonSerializer.DeserializeAsync<OpenRouterModelsResponse>(stream, JsonOptions, cancellationToken);
        Dictionary<string, TokenModelPricing> values = new(StringComparer.OrdinalIgnoreCase);

        foreach (OpenRouterModel model in decoded?.Data ?? [])
        {
            if (string.IsNullOrWhiteSpace(model.Id) || model.Pricing is null)
            {
                continue;
            }

            decimal? input = ParsePrice(model.Pricing.Prompt);
            decimal? output = ParsePrice(model.Pricing.Completion);
            if (input is null && output is null)
            {
                continue;
            }

            values[model.Id] = new TokenModelPricing
            {
                InputCostPerToken = input,
                OutputCostPerToken = output,
            };
        }

        return values;
    }

    private static decimal? ParsePrice(string? value)
    {
        return decimal.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out decimal parsed)
            && parsed >= 0
            ? parsed
            : null;
    }

    private static async Task<T?> TryLoadFreshCacheAsync<T>(string cacheDirectory, string fileName, CancellationToken cancellationToken)
    {
        CachedPricingData<T>? cached = await TryLoadCachedDataAsync<T>(cacheDirectory, fileName, cancellationToken);
        if (cached is null)
        {
            return default;
        }

        DateTimeOffset timestamp = DateTimeOffset.FromUnixTimeSeconds(cached.Timestamp);
        return DateTimeOffset.Now - timestamp <= CacheTtl ? cached.Data : default;
    }

    private static async Task<T?> TryLoadAnyCacheAsync<T>(string cacheDirectory, string fileName, CancellationToken cancellationToken)
    {
        CachedPricingData<T>? cached = await TryLoadCachedDataAsync<T>(cacheDirectory, fileName, cancellationToken);
        return cached is null ? default : cached.Data;
    }

    private static async Task<CachedPricingData<T>?> TryLoadCachedDataAsync<T>(string cacheDirectory, string fileName, CancellationToken cancellationToken)
    {
        string path = Path.Combine(cacheDirectory, fileName);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            await using FileStream stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<CachedPricingData<T>>(stream, JsonOptions, cancellationToken);
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested is false)
        {
            return null;
        }
    }

    private static async Task SaveCacheAsync<T>(string cacheDirectory, string fileName, T data, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(cacheDirectory);
        string path = Path.Combine(cacheDirectory, fileName);
        string tempPath = $"{path}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
        CachedPricingData<T> cached = new()
        {
            Timestamp = DateTimeOffset.Now.ToUnixTimeSeconds(),
            Data = data,
        };

        await using (FileStream stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, cached, JsonOptions, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        File.Move(tempPath, path, overwrite: true);
    }

    private sealed class CachedPricingData<T>
    {
        [JsonPropertyName("timestamp")]
        public long Timestamp { get; set; }

        [JsonPropertyName("data")]
        public T? Data { get; set; }
    }

    private sealed class OpenRouterModelsResponse
    {
        [JsonPropertyName("data")]
        public List<OpenRouterModel> Data { get; set; } = [];
    }

    private sealed class OpenRouterModel
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("pricing")]
        public OpenRouterPricing? Pricing { get; set; }
    }

    private sealed class OpenRouterPricing
    {
        [JsonPropertyName("prompt")]
        public string? Prompt { get; set; }

        [JsonPropertyName("completion")]
        public string? Completion { get; set; }
    }
}
