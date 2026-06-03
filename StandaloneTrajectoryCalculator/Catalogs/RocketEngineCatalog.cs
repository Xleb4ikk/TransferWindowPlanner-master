using System.Text.Json;

namespace StandaloneTrajectoryCalculator;

internal sealed record RocketEngine(
    string Key,
    string DisplayName,
    string PropellantKey,
    double VacuumIspSeconds,
    double VacuumThrustkN,
    double DryMassKg,
    int MaxClusterCount,
    int SelectionRank,
    bool SupportsRestart,
    bool SupportsLongCoast,
    IReadOnlyList<string> SupportedRoles,
    IReadOnlyList<string> SupportedSegments)
{
    public bool SupportsRole(string role) =>
        SupportedRoles.Any(candidate => string.Equals(candidate, role, StringComparison.OrdinalIgnoreCase));

    public bool SupportsSegment(MissionSegment segment) =>
        SupportedSegments.Count == 0
        || SupportedSegments.Any(candidate => string.Equals(candidate, segment.ToString(), StringComparison.OrdinalIgnoreCase));
}

internal static class RocketEngineCatalog
{
    private static readonly Lazy<IReadOnlyList<RocketEngine>> LazyAll = new(LoadEngines);

    public static IReadOnlyList<RocketEngine> All => LazyAll.Value;

    public static RocketEngine? SelectEngine(string propellantKey, string role, MissionSegment segment)
    {
        return FindCandidates(propellantKey, role, segment).FirstOrDefault()
            ?? FindCandidates(propellantKey, role, segment, ignoreRole: true).FirstOrDefault()
            ?? All
                .Where(engine => string.Equals(engine.PropellantKey, propellantKey, StringComparison.OrdinalIgnoreCase))
                .OrderBy(engine => engine.SelectionRank)
                .FirstOrDefault();
    }

    private static IEnumerable<RocketEngine> FindCandidates(string propellantKey, string role, MissionSegment segment, bool ignoreRole = false)
    {
        return All
            .Where(engine => string.Equals(engine.PropellantKey, propellantKey, StringComparison.OrdinalIgnoreCase))
            .Where(engine => ignoreRole || engine.SupportsRole(role))
            .Where(engine => engine.SupportsSegment(segment))
            .OrderBy(engine => engine.SelectionRank)
            .ThenByDescending(engine => engine.VacuumIspSeconds)
            .ThenBy(engine => engine.DryMassKg);
    }

    private static IReadOnlyList<RocketEngine> LoadEngines()
    {
        var path = ResolveCatalogPath();
        var json = File.ReadAllText(path);
        var payload = JsonSerializer.Deserialize<List<RocketEngine>>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (payload is null || payload.Count == 0)
        {
            throw new InvalidOperationException($"Engine catalog '{path}' is empty.");
        }

        var duplicateKeys = payload
            .GroupBy(engine => engine.Key, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        if (duplicateKeys.Length > 0)
        {
            throw new InvalidOperationException($"Engine catalog contains duplicate keys: {string.Join(", ", duplicateKeys)}.");
        }

        return payload;
    }

    private static string ResolveCatalogPath()
    {
        foreach (var seed in EnumerateSearchSeeds())
        {
            foreach (var candidate in EnumerateCandidatePaths(seed))
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        throw new FileNotFoundException("Could not locate engine_catalog.json for the rocket engine catalog.");
    }

    private static IEnumerable<string> EnumerateSearchSeeds()
    {
        foreach (var seed in new[]
                 {
                     Directory.GetCurrentDirectory(),
                     AppContext.BaseDirectory
                 })
        {
            if (!string.IsNullOrWhiteSpace(seed) && Directory.Exists(seed))
            {
                yield return Path.GetFullPath(seed);
            }
        }
    }

    private static IEnumerable<string> EnumerateCandidatePaths(string seed)
    {
        var current = new DirectoryInfo(seed);
        while (current is not null)
        {
            yield return Path.Combine(current.FullName, "engine_catalog.json");
            yield return Path.Combine(current.FullName, "StandaloneTrajectoryCalculator", "engine_catalog.json");
            current = current.Parent;
        }
    }
}
