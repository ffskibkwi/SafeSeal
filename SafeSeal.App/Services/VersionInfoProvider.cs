using System.Reflection;

namespace SafeSeal.App.Services;

public static class VersionInfoProvider
{
    public static string SemanticVersion { get; } = ResolveSemanticVersion();

    private static string ResolveSemanticVersion()
    {
        Assembly assembly = typeof(VersionInfoProvider).Assembly;
        string? informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            string normalized = informational.Split('+')[0].Trim();
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                return normalized.StartsWith("v", StringComparison.OrdinalIgnoreCase)
                    ? normalized
                    : $"v{normalized}";
            }
        }

        Version? version = assembly.GetName().Version;
        if (version is null)
        {
            return "v1.0.0";
        }

        int build = version.Build < 0 ? 0 : version.Build;
        return $"v{version.Major}.{version.Minor}.{build}";
    }
}
