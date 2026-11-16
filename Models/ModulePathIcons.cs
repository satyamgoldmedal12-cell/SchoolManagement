using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SchoolManagement.Models;

public static class ModulePathIcons
{
    public const int MaxKeyLength = 64;

    public static IReadOnlyDictionary<string, string> Options { get; } = LoadLabels("SchoolManagement.FlaticonIcons.json");

    private static readonly IReadOnlyDictionary<string, string> FontAwesomeIcons =
        LoadLabels("SchoolManagement.FontAwesomeIcons.json");

    private static readonly HashSet<string> LegacyKeys = LoadBootstrapIcons();

    public static bool Contains(string? key)
    {
        return key is not null && Options.ContainsKey(key);
    }

    public static string? GetCssClass(string? key)
    {
        if (key is null)
        {
            return null;
        }

        if (Options.ContainsKey(key) || FontAwesomeIcons.ContainsKey(key))
        {
            return key;
        }

        return LegacyKeys.Contains(key) ? $"bi bi-{key}" : null;
    }

    public static string? GetLabel(string? key)
    {
        if (key is null)
        {
            return null;
        }

        return Options.GetValueOrDefault(key) ?? FontAwesomeIcons.GetValueOrDefault(key);
    }

    private static IReadOnlyDictionary<string, string> LoadLabels(string resourceName)
    {
        using var stream = typeof(ModulePathIcons).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"The icon catalog {resourceName} is missing.");
        var labels = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
            ?? throw new InvalidOperationException($"The icon catalog {resourceName} is invalid.");

        return new ReadOnlyDictionary<string, string>(labels);
    }

    private static HashSet<string> LoadBootstrapIcons()
    {
        using var stream = typeof(ModulePathIcons).Assembly.GetManifestResourceStream("SchoolManagement.BootstrapIcons.css")
            ?? throw new InvalidOperationException("The Bootstrap Icons stylesheet is missing.");
        using var reader = new StreamReader(stream);

        return Regex.Matches(reader.ReadToEnd(), @"\.bi-([a-z0-9-]+)::?before")
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
    }
}
