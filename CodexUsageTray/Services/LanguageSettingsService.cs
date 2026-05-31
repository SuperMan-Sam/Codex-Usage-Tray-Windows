using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodexUsageTray.Services;

internal enum LanguageMode
{
    System,
    English,
    ChineseSimplified,
}

internal enum AppLanguage
{
    English,
    ChineseSimplified,
}

internal static class LanguageSettingsService
{
    public const double MinimumTaskbarFontSize = 7.0;
    public const double MaximumTaskbarFontSize = 28.0;
    public const double DefaultTaskbarFontSize = 11.0;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private static AppSettings? _cachedSettings;

    public static event EventHandler? LanguageChanged;

    public static event EventHandler? TaskbarFontSizeChanged;

    public static LanguageMode CurrentMode => Settings.Language;

    public static double TaskbarFontSize => Settings.TaskbarFontSize;

    public static AppLanguage CurrentLanguage => ResolveLanguage(CurrentMode, CultureInfo.CurrentUICulture);

    public static CultureInfo CurrentCulture => CurrentMode switch
    {
        LanguageMode.ChineseSimplified => CultureInfo.GetCultureInfo("zh-CN"),
        LanguageMode.English => CultureInfo.GetCultureInfo("en-US"),
        _ => CultureInfo.CurrentCulture,
    };

    public static void SetMode(LanguageMode mode)
    {
        if (CurrentMode == mode)
        {
            return;
        }

        AppSettings settings = Settings with { Language = mode };
        _cachedSettings = settings;
        SaveSettings(settings);
        LanguageChanged?.Invoke(null, EventArgs.Empty);
    }

    public static void SetTaskbarFontSize(double fontSize)
    {
        double normalizedFontSize = NormalizeTaskbarFontSize(fontSize);
        if (Math.Abs(TaskbarFontSize - normalizedFontSize) < 0.01)
        {
            return;
        }

        AppSettings settings = Settings with { TaskbarFontSize = normalizedFontSize };
        _cachedSettings = settings;
        SaveSettings(settings);
        TaskbarFontSizeChanged?.Invoke(null, EventArgs.Empty);
    }

    public static AppLanguage ResolveLanguage(LanguageMode mode, CultureInfo systemCulture)
    {
        return mode switch
        {
            LanguageMode.ChineseSimplified => AppLanguage.ChineseSimplified,
            LanguageMode.English => AppLanguage.English,
            _ => systemCulture.TwoLetterISOLanguageName.Equals("zh", StringComparison.OrdinalIgnoreCase)
                ? AppLanguage.ChineseSimplified
                : AppLanguage.English,
        };
    }

    private static string SettingsPath => Path.Combine(AppPaths.AppDataFolder, "settings.json");

    private static AppSettings Settings => _cachedSettings ??= LoadSettings();

    private static AppSettings LoadSettings()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new AppSettings();
            }

            string json = File.ReadAllText(SettingsPath);
            AppSettings settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new();
            return settings with { TaskbarFontSize = NormalizeTaskbarFontSize(settings.TaskbarFontSize) };
        }
        catch
        {
            return new AppSettings();
        }
    }

    private static void SaveSettings(AppSettings settings)
    {
        Directory.CreateDirectory(AppPaths.AppDataFolder);
        string tempPath = SettingsPath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(tempPath, SettingsPath, overwrite: true);
    }

    private static double NormalizeTaskbarFontSize(double fontSize)
    {
        if (double.IsNaN(fontSize) || double.IsInfinity(fontSize))
        {
            return DefaultTaskbarFontSize;
        }

        return Math.Round(Math.Clamp(fontSize, MinimumTaskbarFontSize, MaximumTaskbarFontSize), 1);
    }

    private sealed record AppSettings
    {
        public LanguageMode Language { get; init; } = LanguageMode.System;

        public double TaskbarFontSize { get; init; } = DefaultTaskbarFontSize;
    }
}
