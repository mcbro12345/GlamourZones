using System;
using System.Collections.Generic;
using System.Linq;
using Lumina.Excel.Sheets;

namespace GlamourZones.Game;

public enum JobGroup
{
    Tank,
    Healer,
    Melee,
    PhysicalRanged,
    MagicalRanged,
    Crafter,
    Gatherer,
    Other,
}

public sealed record JobInfo(uint Id, string Abbreviation, string Name, JobGroup Group, bool IsBaseClass, int Order)
{
    public uint Icon => 62100 + Id;
}

// One pickable place. Several territory ids can share a name within a zone
// group (Chocobo Square has a few), so they're merged into one entry.
public sealed record Place(string Name, uint[] TerritoryIds, uint RegionId, uint ZoneGroupId, bool IsHousing, bool IsOpenZone);

public sealed record ZoneGroupNode(uint Id, string Name, List<Place> Places);
public sealed record RegionNode(uint Id, string Name, List<ZoneGroupNode> ZoneGroups);
public sealed record ExpansionNode(string Name, List<RegionNode> Regions);

public sealed record WeatherInfo(uint Id, string Name, uint Icon);

// Lookups from the game's sheets, built once.
public static class GameInfo
{
    // Territory uses that are open-world, towns, inns, housing and the
    // like: places a plate can go on. Duties and quest instances are left out.
    private static readonly HashSet<uint> PlaceUses = [0, 1, 2, 13, 14, 20, 21, 23, 30, 49, 56, 60];
    private static readonly HashSet<uint> HousingUses = [13, 14];

    private static List<JobInfo>? jobs;
    private static List<ExpansionNode>? expansions;
    private static Dictionary<uint, (uint Region, uint ZoneGroup)>? territoryParents;
    private static List<WeatherInfo>? weathers;
    private static readonly Dictionary<uint, string> PlaceNames = [];

    public static IReadOnlyList<JobInfo> Jobs => jobs ??= LoadJobs();
    public static IReadOnlyList<ExpansionNode> Expansions => expansions ??= LoadPlaces();
    public static IReadOnlyList<WeatherInfo> Weathers => weathers ??= LoadWeathers();

    public static JobInfo? Job(uint id) => Jobs.FirstOrDefault(j => j.Id == id);

    public static (uint Region, uint ZoneGroup) Parents(uint territory)
    {
        territoryParents ??= Services.Data.GetExcelSheet<TerritoryType>()
            .ToDictionary(t => t.RowId, t => (t.PlaceNameRegion.RowId, t.PlaceNameZone.RowId));
        return territoryParents.GetValueOrDefault(territory);
    }

    public static string PlaceName(uint id)
    {
        if (id == 0)
            return "(none)";
        if (!PlaceNames.TryGetValue(id, out var name))
        {
            name = Services.Data.GetExcelSheet<Lumina.Excel.Sheets.PlaceName>().GetRowOrDefault(id)?.Name.ToString() ?? string.Empty;
            PlaceNames[id] = name = name.Length == 0 ? $"#{id}" : name;
        }
        return name;
    }

    public static string TerritoryName(uint territory) =>
        Services.Data.GetExcelSheet<TerritoryType>().GetRowOrDefault(territory) is { } row ? PlaceName(row.PlaceName.RowId) : $"#{territory}";

    public static string WeatherName(uint id) => Weathers.FirstOrDefault(w => w.Id == id)?.Name ?? $"#{id}";

    public static string ItemName(uint id) =>
        id == 0 ? string.Empty : Services.Data.GetExcelSheet<Item>().GetRowOrDefault(id % 1_000_000)?.Name.ToString() ?? $"#{id}";

    private static List<JobInfo> LoadJobs()
    {
        var list = new List<JobInfo>();
        foreach (var row in Services.Data.GetExcelSheet<ClassJob>())
        {
            var abbreviation = row.Abbreviation.ToString();
            if (row.RowId == 0 || abbreviation.Length == 0)
                continue;
            var name = row.Name.ToString();
            var group = row.ClassJobCategory.RowId switch
            {
                33 => JobGroup.Crafter,
                32 => JobGroup.Gatherer,
                _ => row.Role switch
                {
                    1 => JobGroup.Tank,
                    4 => JobGroup.Healer,
                    2 => JobGroup.Melee,
                    3 when row.PrimaryStat == 4 => JobGroup.MagicalRanged,
                    3 => JobGroup.PhysicalRanged,
                    _ => JobGroup.Other,
                },
            };
            var isBase = row.JobIndex == 0 && group is not (JobGroup.Crafter or JobGroup.Gatherer);
            var title = name.Length == 0 ? abbreviation : char.ToUpperInvariant(name[0]) + name[1..];
            list.Add(new JobInfo(row.RowId, abbreviation, title, group, isBase, row.UIPriority));
        }
        return [.. list.OrderBy(j => j.Group).ThenBy(j => j.IsBaseClass).ThenBy(j => j.Order).ThenBy(j => j.Id)];
    }

    // Expansion > region > zone group > place. Housing is listed under its
    // own heading since the game files every ward as A Realm Reborn.
    private static List<ExpansionNode> LoadPlaces()
    {
        var rows = Services.Data.GetExcelSheet<TerritoryType>()
            .Where(t => t.PlaceName.RowId != 0 && t.ContentFinderCondition.RowId == 0 && PlaceUses.Contains(t.TerritoryIntendedUse.RowId))
            .Where(t => t.PlaceName.Value.Name.ToString().Length > 0 && t.PlaceNameRegion.RowId != 0)
            .Select(t => (Row: t, Housing: HousingUses.Contains(t.TerritoryIntendedUse.RowId)))
            .OrderBy(t => t.Row.RowId)
            .ToList();

        var result = new List<ExpansionNode>();
        var expansionGroups = rows.GroupBy(r => r.Housing ? uint.MaxValue : r.Row.ExVersion.RowId).OrderBy(g => g.Key);
        foreach (var expansion in expansionGroups)
        {
            var expansionName = expansion.Key == uint.MaxValue
                ? "Housing"
                : expansion.First().Row.ExVersion.Value.Name.ToString();
            var regions = new List<RegionNode>();
            foreach (var region in expansion.GroupBy(r => r.Row.PlaceNameRegion.RowId))
            {
                var zoneGroups = new List<ZoneGroupNode>();
                foreach (var zone in region.GroupBy(r => r.Row.PlaceNameZone.RowId))
                {
                    var places = zone
                        .GroupBy(r => r.Row.PlaceName.Value.Name.ToString())
                        .Select(g => new Place(g.Key, [.. g.Select(r => r.Row.RowId)], region.Key, zone.Key, g.First().Housing, g.First().Row.TerritoryIntendedUse.RowId == 1))
                        .ToList();
                    var zoneName = PlaceName(zone.Key);
                    if (zoneName.Contains('?') || zoneName.StartsWith('#'))
                        zoneName = PlaceName(region.Key);
                    zoneGroups.Add(new ZoneGroupNode(zone.Key, zoneName, places));
                }
                regions.Add(new RegionNode(region.Key, PlaceName(region.Key), zoneGroups));
            }
            result.Add(new ExpansionNode(expansionName, regions));
        }
        return result;
    }

    private static List<WeatherInfo> LoadWeathers() =>
        [.. Services.Data.GetExcelSheet<Weather>()
            .Where(w => w.RowId != 0 && w.Name.ToString().Length > 0)
            .GroupBy(w => w.Name.ToString())
            .Select(g => new WeatherInfo(g.First().RowId, g.Key, (uint)g.First().Icon))
            .OrderBy(w => w.Name, StringComparer.OrdinalIgnoreCase)];

    private static Dictionary<uint, uint>? canonicalWeather;

    // The sheet repeats some weather names under several ids; rules store
    // the first id with a name, so the current weather is mapped to it too.
    public static uint CanonicalWeather(uint id)
    {
        if (canonicalWeather == null)
        {
            var byName = Weathers.ToDictionary(w => w.Name, w => w.Id);
            canonicalWeather = Services.Data.GetExcelSheet<Weather>()
                .ToDictionary(w => w.RowId, w => byName.GetValueOrDefault(w.Name.ToString(), w.RowId));
        }
        return canonicalWeather.GetValueOrDefault(id, id);
    }
}
