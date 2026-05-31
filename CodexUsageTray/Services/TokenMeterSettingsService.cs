using System.Text.Json;
using System.Text.Json.Serialization;
using CodexUsageTray.Core.Models;

namespace CodexUsageTray.Services;

internal static class TokenMeterSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public static string SettingsPath => Path.Combine(AppPaths.AppDataFolder, "token-meter-settings.json");

    public static TokenUsageRange CurrentRange { get; private set; } = Load().Range;

    public static void SetRange(TokenUsageRange range)
    {
        if (CurrentRange == range)
        {
            return;
        }

        CurrentRange = range;
        Save(new TokenMeterSettings { Range = range });
    }

    private static TokenMeterSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new TokenMeterSettings();
            }

            string json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<TokenMeterSettings>(json, JsonOptions) ?? new TokenMeterSettings();
        }
        catch
        {
            return new TokenMeterSettings();
        }
    }

    private static void Save(TokenMeterSettings settings)
    {
        Directory.CreateDirectory(AppPaths.AppDataFolder);
        string tempPath = SettingsPath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(tempPath, SettingsPath, overwrite: true);
    }

    private sealed class TokenMeterSettings
    {
        [JsonConverter(typeof(JsonStringEnumConverter<TokenUsageRange>))]
        public TokenUsageRange Range { get; set; } = TokenUsageRange.AllHistory;
    }
}
