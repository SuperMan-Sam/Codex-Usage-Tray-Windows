namespace CodexUsageTray;

internal static class AppPaths
{
    public static string AppDataFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CodexUsageTray");

    public static string WebViewUserDataFolder { get; } = Path.Combine(AppDataFolder, "WebView2");

    public static string PricingCacheFolder { get; } = Path.Combine(AppDataFolder, "Pricing");
}
