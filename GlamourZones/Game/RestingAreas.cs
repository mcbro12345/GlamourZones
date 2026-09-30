using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Lumina.Data.Files;
using Lumina.Data.Parsing.Layer;
using Lumina.Excel.Sheets;

namespace GlamourZones.Game;

// Which zones have resting areas, the only places plates can go on. Read
// from the zones' layout files, where each map range says whether it gives
// rest bonus. Zones whose files can't be read are left unknown.
public static class RestingAreas
{
    private static readonly string[] LayoutFiles = ["planmap.lgb", "planevent.lgb", "bg.lgb", "planner.lgb", "planlive.lgb"];

    // Areas are the named resting areas (like Falcon's Nest) as PlaceName ids.
    public sealed record Info(bool HasRestingArea, uint[] Areas);

    private static volatile Dictionary<uint, Info>? results;

    public static bool Ready => results != null;

    // Null while loading or when the zone's files couldn't be read.
    public static Info? Get(uint territory) => results?.GetValueOrDefault(territory);

    // False only for zones known to have no resting area at all.
    public static bool CanApplyIn(uint territory) => Get(territory) is not { HasRestingArea: false };

    public static void Load(IReadOnlyCollection<uint> territories)
    {
        var sheet = Services.Data.GetExcelSheet<TerritoryType>();
        var paths = territories
            .Select(t => (Id: t, Bg: sheet.GetRowOrDefault(t)?.Bg.ToString() ?? string.Empty))
            .Where(t => t.Bg.Contains("/level/"))
            .ToList();
        Task.Run(() =>
        {
            var found = new Dictionary<uint, Info>();
            foreach (var (id, bg) in paths)
            {
                try
                {
                    if (Read(bg) is { } info)
                        found[id] = info;
                }
                catch (Exception e)
                {
                    Services.Log.Debug(e, $"Couldn't read the layout of territory {id}");
                }
            }
            results = found;
            Services.Log.Debug($"Resting areas known for {found.Count} of {paths.Count} zones");
        });
    }

    private static Info? Read(string bg)
    {
        var folder = $"bg/{bg[..bg.LastIndexOf("/level/", StringComparison.Ordinal)]}/level/";
        var any = false;
        var resting = false;
        var areas = new HashSet<uint>();
        foreach (var file in LayoutFiles)
        {
            var lgb = Services.Data.GetFile<LgbFile>(folder + file);
            if (lgb == null)
                continue;
            any = true;
            foreach (var layer in lgb.Layers)
            {
                foreach (var instance in layer.InstanceObjects)
                {
                    if (instance.Object is not LayerCommon.MapRangeInstanceObject range || range.RestBonusEnabled == 0 || range.RestBonusEffective == 0)
                        continue;
                    resting = true;
                    var area = range.PlaceNameSpot != 0 ? range.PlaceNameSpot : range.PlaceNameBlock;
                    if (area != 0)
                        areas.Add(area);
                }
            }
        }
        return any ? new Info(resting, [.. areas]) : null;
    }
}
