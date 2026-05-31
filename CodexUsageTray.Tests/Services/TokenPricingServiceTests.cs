using System.Net;
using System.Text;
using CodexUsageTray.Core.Models;
using CodexUsageTray.Core.Services.TokenMeter;

namespace CodexUsageTray.Tests.Services;

[TestClass]
public sealed class TokenPricingServiceTests
{
    [TestMethod]
    public void PricingDataset_CalculatesTieredCost()
    {
        TokenPricingDataset dataset = new(new Dictionary<string, TokenModelPricing>
        {
            ["gpt-test"] = new()
            {
                InputCostPerToken = 0.000001m,
                InputCostPerTokenAbove200KTokens = 0.000002m,
                OutputCostPerToken = 0.000003m,
                CacheReadInputTokenCost = 0.0000001m,
            },
        }, null, DateTimeOffset.Now, false, "test");

        TokenCostEstimate? estimate = dataset.Calculate(
            "gpt-test",
            "openai",
            new TokenBreakdown(250_000, 10_000, 50_000, 0, 0));

        Assert.IsNotNull(estimate);
        Assert.AreEqual(0.335000m, estimate.Cost);
    }

    [TestMethod]
    public void PricingDataset_WhenUnknownModel_ReturnsNull()
    {
        TokenPricingDataset dataset = new([], [], DateTimeOffset.Now, false, "test");

        TokenCostEstimate? estimate = dataset.Calculate("missing-model", "openai", new TokenBreakdown(1, 0, 0, 0, 0));

        Assert.IsNull(estimate);
    }

    [TestMethod]
    public void PricingDataset_WhenLiteLlmHasCaseDuplicate_DoesNotThrow()
    {
        TokenPricingDataset dataset = new(new Dictionary<string, TokenModelPricing>
        {
            ["together_ai/BAAI/bge-base-en-v1.5"] = new() { InputCostPerToken = 0.000001m },
            ["together_ai/baai/bge-base-en-v1.5"] = new() { InputCostPerToken = 0.000002m },
        }, null, DateTimeOffset.Now, false, "test");

        Assert.AreEqual(1, dataset.Snapshot.LiteLlmModelCount);
    }

    [TestMethod]
    public async Task GetPricingAsync_FetchesOnlineAndCaches()
    {
        using TempCache cache = new();
        using HttpClient client = new(new StaticPricingHandler(false));
        TokenPricingService service = new(client);

        TokenPricingDataset dataset = await service.GetPricingAsync(cache.Path);

        Assert.AreEqual(1, dataset.Snapshot.LiteLlmModelCount);
        Assert.AreEqual(1, dataset.Snapshot.OpenRouterModelCount);
        Assert.IsFalse(dataset.Snapshot.UsedStaleCache);
        Assert.IsTrue(File.Exists(System.IO.Path.Combine(cache.Path, "pricing-litellm.json")));
        Assert.IsTrue(File.Exists(System.IO.Path.Combine(cache.Path, "pricing-openrouter.json")));
    }

    [TestMethod]
    public async Task GetPricingAsync_WhenOnlineFails_UsesStaleCache()
    {
        using TempCache cache = new();
        cache.WriteStaleCaches();
        using HttpClient client = new(new StaticPricingHandler(true));
        TokenPricingService service = new(client);

        TokenPricingDataset dataset = await service.GetPricingAsync(cache.Path);

        Assert.IsTrue(dataset.Snapshot.UsedStaleCache);
        Assert.AreEqual(1, dataset.Snapshot.LiteLlmModelCount);
        Assert.AreEqual(1, dataset.Snapshot.OpenRouterModelCount);
    }

    private sealed class StaticPricingHandler(bool fail) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (fail)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            }

            string body = request.RequestUri?.Host.Contains("openrouter", StringComparison.OrdinalIgnoreCase) == true
                ? """{"data":[{"id":"openai/gpt-router","pricing":{"prompt":"0.000003","completion":"0.000004"}}]}"""
                : """{"gpt-test":{"input_cost_per_token":0.000001,"output_cost_per_token":0.000002}}""";

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class TempCache : IDisposable
    {
        public TempCache()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "CodexUsageTrayPricingTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void WriteStaleCaches()
        {
            long staleTimestamp = DateTimeOffset.Now.AddDays(-2).ToUnixTimeSeconds();
            File.WriteAllText(
                System.IO.Path.Combine(Path, "pricing-litellm.json"),
                $"{{\"timestamp\":{staleTimestamp},\"data\":{{\"gpt-stale\":{{\"input_cost_per_token\":0.000001,\"output_cost_per_token\":0.000002}}}}}}");
            File.WriteAllText(
                System.IO.Path.Combine(Path, "pricing-openrouter.json"),
                $"{{\"timestamp\":{staleTimestamp},\"data\":{{\"openai/gpt-router\":{{\"input_cost_per_token\":0.000003,\"output_cost_per_token\":0.000004}}}}}}");
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
