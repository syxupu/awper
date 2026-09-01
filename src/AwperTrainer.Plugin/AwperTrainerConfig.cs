using AwperTrainer.Core;
using CounterStrikeSharp.API.Core;
using System.Text.Json.Serialization;

namespace AwperTrainer.Plugin;

public sealed class AwperTrainerConfig : BasePluginConfig
{
    [JsonPropertyName("AllowedMaps")]
    public string[] AllowedMaps { get; set; } =
    [
        "de_dust2",
        "de_inferno",
        "de_mirage",
        "de_anubis",
        "de_ancient",
        "de_nuke",
        "de_cache"
    ];

    [JsonPropertyName("EnableMotionCsv")]
    public bool EnableMotionCsv { get; set; }

    [JsonPropertyName("MotionCsvSampleEveryTicks")]
    public int MotionCsvSampleEveryTicks { get; set; } = 1;

    internal static string[] NormalizeAllowedMaps(string[]? values)
    {
        if (values is null || values.Length == 0)
            throw new InvalidDataException("AllowedMaps must contain at least one map.");
        var normalized = values.Select(MapPolicy.Normalize).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (normalized.Any(map => !map.StartsWith("de_", StringComparison.Ordinal) ||
            map.Any(c => !(char.IsAsciiLetterOrDigit(c) || c == '_'))))
            throw new InvalidDataException("AllowedMaps entries must be safe de_* map names.");
        return normalized;
    }
}
