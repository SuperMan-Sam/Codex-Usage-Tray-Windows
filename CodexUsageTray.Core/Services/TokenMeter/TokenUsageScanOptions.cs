namespace CodexUsageTray.Core.Services.TokenMeter;

public sealed class TokenUsageScanOptions
{
    public string HomeDirectory { get; init; } = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public string? XdgDataDirectory { get; init; }

    public string? ConfigDirectory { get; init; }

    public string? LocalAppDataDirectory { get; init; }

    public string? RoamingAppDataDirectory { get; init; }

    public Dictionary<string, string> EnvironmentOverrides { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public string ResolveRoot(TokenClientDefinition client)
    {
        return client.Root switch
        {
            TokenClientRoot.Home => HomeDirectory,
            TokenClientRoot.XdgData => ResolveXdgDataRoot(),
            TokenClientRoot.Config => ResolveConfigRoot(),
            TokenClientRoot.EnvVar => ResolveEnvironmentRoot(client),
            _ => HomeDirectory,
        };
    }

    public string ResolveClientDirectory(TokenClientDefinition client)
    {
        return Path.GetFullPath(Path.Combine(ResolveRoot(client), client.RelativePath));
    }

    public IEnumerable<string> ResolveClientDirectories(TokenClientDefinition client)
    {
        string root = ResolveRoot(client);
        yield return Path.GetFullPath(Path.Combine(root, client.RelativePath));

        if (client.AdditionalRelativePaths is null)
        {
            yield break;
        }

        foreach (string relativePath in client.AdditionalRelativePaths)
        {
            if (!string.IsNullOrWhiteSpace(relativePath))
            {
                yield return Path.GetFullPath(Path.Combine(root, relativePath));
            }
        }
    }

    private string ResolveXdgDataRoot()
    {
        if (!string.IsNullOrWhiteSpace(XdgDataDirectory))
        {
            return XdgDataDirectory;
        }

        string? env = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (!string.IsNullOrWhiteSpace(env))
        {
            return env;
        }

        string? localAppData = LocalAppDataDirectory;
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        }

        return string.IsNullOrWhiteSpace(localAppData)
            ? Path.Combine(HomeDirectory, ".local", "share")
            : localAppData;
    }

    private string ResolveConfigRoot()
    {
        if (!string.IsNullOrWhiteSpace(ConfigDirectory))
        {
            return ConfigDirectory;
        }

        string? explicitConfig = Environment.GetEnvironmentVariable("TOKSCALE_CONFIG_DIR");
        if (!string.IsNullOrWhiteSpace(explicitConfig))
        {
            return explicitConfig;
        }

        string? appData = RoamingAppDataDirectory;
        if (string.IsNullOrWhiteSpace(appData))
        {
            appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        }

        return string.IsNullOrWhiteSpace(appData)
            ? Path.Combine(HomeDirectory, ".config", "tokscale")
            : Path.Combine(appData, "tokscale");
    }

    private string ResolveEnvironmentRoot(TokenClientDefinition client)
    {
        if (!string.IsNullOrWhiteSpace(client.EnvironmentVariable)
            && EnvironmentOverrides.TryGetValue(client.EnvironmentVariable, out string? overrideValue)
            && !string.IsNullOrWhiteSpace(overrideValue))
        {
            return overrideValue;
        }

        if (!string.IsNullOrWhiteSpace(client.EnvironmentVariable))
        {
            string? env = Environment.GetEnvironmentVariable(client.EnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(env))
            {
                return env;
            }
        }

        return Path.Combine(HomeDirectory, client.FallbackRelativePath ?? string.Empty);
    }
}
