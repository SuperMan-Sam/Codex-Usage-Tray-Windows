using System.Text.Json;
using CodexUsageTray.Core.Models;

namespace CodexUsageTray.Services;

public static class UsageSnapshotCache
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public static string SnapshotPath => Path.Combine(AppPaths.AppDataFolder, "usage-snapshot.json");

    public static void Save(UsageSnapshot snapshot)
    {
        Directory.CreateDirectory(AppPaths.AppDataFolder);
        string tempPath = SnapshotPath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(snapshot, JsonOptions));
        File.Move(tempPath, SnapshotPath, overwrite: true);
    }

    public static UsageSnapshot LoadOrUnknown()
    {
        try
        {
            if (!File.Exists(SnapshotPath))
            {
                return UsageSnapshot.Unknown(DateTimeOffset.Now);
            }

            string json = File.ReadAllText(SnapshotPath);
            return JsonSerializer.Deserialize<UsageSnapshot>(json) ?? UsageSnapshot.Unknown(DateTimeOffset.Now);
        }
        catch
        {
            return UsageSnapshot.Unknown(DateTimeOffset.Now);
        }
    }
}
