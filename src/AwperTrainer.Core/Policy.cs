using System.Text.RegularExpressions;

namespace AwperTrainer.Core;

public sealed class MapPolicy
{
    public static readonly IReadOnlyList<string> PreferredPool =
    [
        "de_dust2", "de_inferno", "de_mirage", "de_anubis", "de_ancient", "de_nuke", "de_cache"
    ];

    public static readonly IReadOnlySet<string> SeasonFiveActiveDuty =
        new HashSet<string>(PreferredPool, StringComparer.OrdinalIgnoreCase);

    private readonly IReadOnlySet<string> _allowed;
    public MapPolicy(IEnumerable<string>? allowed = null)
        => _allowed = new HashSet<string>((allowed ?? SeasonFiveActiveDuty).Select(Normalize), StringComparer.OrdinalIgnoreCase);
    public bool IsAllowed(string mapName) => _allowed.Contains(Normalize(mapName));
    public IReadOnlyCollection<string> AllowedMaps => PreferredPool.Where(_allowed.Contains)
        .Concat(_allowed.Where(map => !PreferredPool.Contains(map, StringComparer.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal))
        .ToArray();
    public static string Normalize(string mapName) => mapName.Trim().ToLowerInvariant();
    public static string NormalizeAlias(string mapName)
    {
        var normalized = Normalize(mapName);
        return normalized.StartsWith("de_", StringComparison.Ordinal) ? normalized : $"de_{normalized}";
    }
}

public static partial class ProfileNames
{
    public const int MaxLength = 64;
    [GeneratedRegex("^[a-zA-Z0-9_-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeNamePattern();

    public static string Normalize(string value)
    {
        var name = value.Trim();
        if (name.Length is < 1 or > MaxLength || !SafeNamePattern().IsMatch(name))
            throw new ArgumentException("Profile name must be 1-64 ASCII letters, digits, '_' or '-'.", nameof(value));
        return name;
    }
}
