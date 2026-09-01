using System.Text.Json;

namespace AwperTrainer.Core;

public sealed class ProfileRepository
{
    private readonly string _root;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public ProfileRepository(string root) => _root = Path.GetFullPath(root);

    public async Task SaveAsync(AwperProfile profile, CancellationToken cancellationToken = default)
    {
        var path = Resolve(profile.MapName, profile.ProfileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, profile, _json, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public async Task<AwperProfile> LoadAsync(string mapName, string profileName, CancellationToken cancellationToken = default)
    {
        var path = Resolve(mapName, profileName);
        await using var stream = File.OpenRead(path);
        var profile = await JsonSerializer.DeserializeAsync<AwperProfile>(stream, _json, cancellationToken)
            ?? throw new InvalidDataException("Profile JSON was empty.");
        if (profile.SchemaVersion != AwperProfile.CurrentSchemaVersion)
            throw new InvalidDataException($"Unsupported schemaVersion {profile.SchemaVersion}.");
        if (!string.Equals(MapPolicy.Normalize(profile.MapName), MapPolicy.Normalize(mapName), StringComparison.Ordinal))
            throw new InvalidDataException("Stored profile map does not match its directory.");
        // Alpha.1-alpha.3 used 250 as a generic usercmd ceiling. Profiles at
        // that legacy default migrate in memory to the AK-47's real full-speed
        // ceiling so existing angles remain loadable after the movement fix.
        if (profile.Training.TargetSpeed == 250f)
            profile = profile with
            {
                Training = profile.Training with { TargetSpeed = TrainingSettings.MaximumAk47Speed }
            };
        return profile;
    }

    public IReadOnlyList<string> List(string mapName)
    {
        var directory = Path.GetDirectoryName(Resolve(mapName, "placeholder"))!;
        if (!Directory.Exists(directory)) return [];
        return Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly)
            .Select(path => Path.GetFileNameWithoutExtension(path)!).Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public bool Delete(string mapName, string profileName)
    {
        var path = Resolve(mapName, profileName);
        if (!File.Exists(path)) return false;
        File.Delete(path);
        return true;
    }

    public string Resolve(string mapName, string profileName)
    {
        var map = MapPolicy.Normalize(mapName);
        if (!map.StartsWith("de_", StringComparison.Ordinal) || map.Any(c => !(char.IsAsciiLetterOrDigit(c) || c == '_')))
            throw new ArgumentException("Unsafe map name.", nameof(mapName));
        var name = ProfileNames.Normalize(profileName);
        var path = Path.GetFullPath(Path.Combine(_root, map, name + ".json"));
        var expectedRoot = _root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(expectedRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Profile path escaped repository root.");
        return path;
    }
}
