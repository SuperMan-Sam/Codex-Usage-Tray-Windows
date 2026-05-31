namespace CodexUsageTray.Core.Models;

public sealed record PricingSnapshot(
    DateTimeOffset CapturedAt,
    bool UsedStaleCache,
    int LiteLlmModelCount,
    int OpenRouterModelCount,
    string Status);
