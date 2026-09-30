using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.Graphics.Environment;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace GlamourZones.Game;

// Everything the rules look at, read once per check.
public sealed record Snapshot
{
    public ulong ContentId { get; init; }
    public string Character { get; init; } = string.Empty;
    public uint Job { get; init; }
    public uint Territory { get; init; }
    public uint Region { get; init; }
    public uint ZoneGroup { get; init; }
    public uint Area { get; init; }
    public uint SubArea { get; init; }
    public uint Weather { get; init; }
    public int EorzeaHour { get; init; }
    public int EorzeaMinute { get; init; }
    public bool InRestingArea { get; init; }
    public bool GameAllowsPlates { get; init; }
    public bool InCombat { get; init; }
    public bool Busy { get; init; }
    public HouseKind Houses { get; init; }

    public static unsafe Snapshot? Take()
    {
        var player = Services.PlayerState;
        if (!Services.ClientState.IsLoggedIn || !player.IsLoaded)
            return null;

        var territory = Services.ClientState.TerritoryType;
        var (region, zoneGroup) = GameInfo.Parents(territory);
        var info = TerritoryInfo.Instance();
        var env = EnvManager.Instance();
        var eorzea = FFXIVClientStructs.FFXIV.Client.System.Framework.Framework.Instance()->ClientTime.EorzeaTime;
        var mirage = MirageManager.Instance();
        var condition = Services.Condition;

        return new Snapshot
        {
            ContentId = player.ContentId,
            Character = $"{player.CharacterName}@{player.HomeWorld.ValueNullable?.Name}",
            Job = player.ClassJob.RowId,
            Territory = territory,
            Region = region,
            ZoneGroup = zoneGroup,
            Area = info == null ? 0 : info->AreaPlaceNameId,
            SubArea = info == null ? 0 : info->SubAreaPlaceNameId,
            Weather = env == null ? 0 : GameInfo.CanonicalWeather(env->ActiveWeather),
            EorzeaHour = (int)(eorzea / 3600 % 24),
            EorzeaMinute = (int)(eorzea / 60 % 60),
            InRestingArea = info != null && info->InSanctuary,
            GameAllowsPlates = UIGlobals.CanApplyGlamourPlates(false) && (mirage == null || !mirage->IsApplyingGlamourPlate),
            Houses = Housing.Current(),
            InCombat = condition[ConditionFlag.InCombat],
            Busy = condition[ConditionFlag.BetweenAreas] || condition[ConditionFlag.BetweenAreas51]
                || condition[ConditionFlag.OccupiedInCutSceneEvent] || condition[ConditionFlag.WatchingCutscene]
                || condition[ConditionFlag.WatchingCutscene78] || condition[ConditionFlag.Occupied]
                || condition[ConditionFlag.OccupiedInEvent] || condition[ConditionFlag.OccupiedInQuestEvent]
                || condition[ConditionFlag.Casting] || condition[ConditionFlag.Crafting]
                || condition[ConditionFlag.BoundByDuty] || condition[ConditionFlag.Jumping],
        };
    }
}

public static class RuleMatching
{
    public static bool Matches(this Rule rule, Snapshot now)
    {
        if (rule.Characters.Count > 0 && !rule.Characters.ContainsKey(now.ContentId))
            return false;
        if (rule.Jobs.Count > 0 && !rule.Jobs.Contains(now.Job))
            return false;
        if (rule.HasLocations)
        {
            var here = rule.Regions.Contains(now.Region)
                || rule.ZoneGroups.Contains(now.ZoneGroup)
                || rule.Territories.Contains(now.Territory)
                || (now.Area != 0 && rule.Areas.Contains(now.Area))
                || (now.SubArea != 0 && rule.Areas.Contains(now.SubArea));
            if (here == rule.ExcludeLocations)
                return false;
        }
        if (rule.Houses != HouseKind.None && (rule.Houses & now.Houses) == 0)
            return false;
        if (rule.UseTime && rule.TimeStart != rule.TimeEnd)
        {
            var inWindow = rule.TimeStart < rule.TimeEnd
                ? now.EorzeaHour >= rule.TimeStart && now.EorzeaHour < rule.TimeEnd
                : now.EorzeaHour >= rule.TimeStart || now.EorzeaHour < rule.TimeEnd;
            if (!inWindow)
                return false;
        }
        if (rule.Weathers.Count > 0 && !rule.Weathers.Contains(now.Weather))
            return false;
        return true;
    }
}
