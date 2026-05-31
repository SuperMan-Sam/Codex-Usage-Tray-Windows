using System.Text.Json.Serialization;

namespace CodexUsageTray.Core.Services.TokenMeter;

public sealed class TokenModelPricing
{
    [JsonPropertyName("input_cost_per_token")]
    public decimal? InputCostPerToken { get; set; }

    [JsonPropertyName("input_cost_per_token_above_128k_tokens")]
    public decimal? InputCostPerTokenAbove128KTokens { get; set; }

    [JsonPropertyName("input_cost_per_token_above_200k_tokens")]
    public decimal? InputCostPerTokenAbove200KTokens { get; set; }

    [JsonPropertyName("input_cost_per_token_above_256k_tokens")]
    public decimal? InputCostPerTokenAbove256KTokens { get; set; }

    [JsonPropertyName("input_cost_per_token_above_272k_tokens")]
    public decimal? InputCostPerTokenAbove272KTokens { get; set; }

    [JsonPropertyName("output_cost_per_token")]
    public decimal? OutputCostPerToken { get; set; }

    [JsonPropertyName("output_cost_per_token_above_128k_tokens")]
    public decimal? OutputCostPerTokenAbove128KTokens { get; set; }

    [JsonPropertyName("output_cost_per_token_above_200k_tokens")]
    public decimal? OutputCostPerTokenAbove200KTokens { get; set; }

    [JsonPropertyName("output_cost_per_token_above_256k_tokens")]
    public decimal? OutputCostPerTokenAbove256KTokens { get; set; }

    [JsonPropertyName("output_cost_per_token_above_272k_tokens")]
    public decimal? OutputCostPerTokenAbove272KTokens { get; set; }

    [JsonPropertyName("cache_read_input_token_cost")]
    public decimal? CacheReadInputTokenCost { get; set; }

    [JsonPropertyName("cache_read_input_token_cost_above_200k_tokens")]
    public decimal? CacheReadInputTokenCostAbove200KTokens { get; set; }

    [JsonPropertyName("cache_read_input_token_cost_above_272k_tokens")]
    public decimal? CacheReadInputTokenCostAbove272KTokens { get; set; }

    [JsonPropertyName("cache_creation_input_token_cost")]
    public decimal? CacheCreationInputTokenCost { get; set; }

    [JsonPropertyName("cache_creation_input_token_cost_above_200k_tokens")]
    public decimal? CacheCreationInputTokenCostAbove200KTokens { get; set; }
}
