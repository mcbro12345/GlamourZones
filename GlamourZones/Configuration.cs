using System;
using System.Collections.Generic;
using Dalamud.Configuration;

namespace GlamourZones;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    // Special plate values for fallbacks.
    public const int LeaveAlone = 0;
    public const int LinkedPlate = -1; // the gear set's own linked plate
    public const int UseDefault = -2;  // per-job fallback defers to the default

    public int Version { get; set; } = 2;

    public bool Enabled { get; set; } = true;
    public List<Rule> Rules { get; set; } = [];

    // Groups switched off; their rules are ignored.
    public HashSet<string> DisabledGroups { get; set; } = [];

    // Your own names for the plates; the game doesn't let you name them.
    public Dictionary<int, string> PlateNames { get; set; } = [];

    // Used when no rule matches: a plate number, LeaveAlone or LinkedPlate.
    public int FallbackPlate { get; set; }

    // Per-job fallbacks by ClassJob id; missing jobs use FallbackPlate.
    public Dictionary<uint, int> JobFallbacks { get; set; } = [];

    // What was last put on each character, so the same plate isn't
    // re-applied (which re-equips the gear set) when it's already on.
    public Dictionary<ulong, AppliedState> LastApplied { get; set; } = [];

    // Behavior
    public float DelaySeconds { get; set; } = 1.5f;
    public bool WaitForRestingArea { get; set; } = true;
    public bool ReapplyOnZoneChange { get; set; } = true;
    public bool ReapplyOnJobChange { get; set; } = true;
    public bool ReactInsideZone { get; set; } = true; // area, time and weather changes
    public bool SkipInCombat { get; set; } = true;
    public bool SkipIfAlreadyOn { get; set; } = true;
    public bool UseJobGearset { get; set; } = true;

    // Feedback
    public bool ChatNotify { get; set; } = true;
    public bool ShowDtr { get; set; } = true;
    public bool GlamourerNoticeDismissed { get; set; }

    // Keybinds
    public Keybind ToggleKey { get; set; } = new();
    public Keybind ApplyKey { get; set; } = new();

    public bool IsActive(Rule rule) => rule.Enabled && !DisabledGroups.Contains(rule.Group);

    public int FallbackFor(uint job) =>
        JobFallbacks.TryGetValue(job, out var plate) && plate != UseDefault ? plate : FallbackPlate;

    public void Save() => Services.PluginInterface.SavePluginConfig(this);
}

[Serializable]
public sealed class AppliedState
{
    public int Plate { get; set; }
    public int Gearset { get; set; }
    public string Fingerprint { get; set; } = string.Empty;
}

[Flags]
public enum HouseKind
{
    None = 0,
    MyHouse = 1,
    MyFreeCompanyHouse = 2,
    MyApartment = 4,
    MyPrivateChambers = 8,
    SomeoneElses = 16,
}

[Serializable]
public sealed class Rule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New rule";
    public string Group { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public int Plate { get; set; } = 1;

    // Jobs: empty means every job.
    public HashSet<uint> Jobs { get; set; } = [];

    // Locations: empty means everywhere. A rule matches when any of these
    // match: whole regions, zone groups (a city with its districts, inns and
    // housing), single territories, or the areas shown under the minimap.
    public HashSet<uint> Regions { get; set; } = [];
    public HashSet<uint> ZoneGroups { get; set; } = [];
    public HashSet<uint> Territories { get; set; } = [];
    public HashSet<uint> Areas { get; set; } = [];
    public bool ExcludeLocations { get; set; } // "anywhere except these"

    // Inside a house: None means no housing condition.
    public HouseKind Houses { get; set; }

    // Eorzea time, in hours. Start == End means any time; wraps past midnight.
    public bool UseTime { get; set; }
    public int TimeStart { get; set; } = 18;
    public int TimeEnd { get; set; } = 6;

    public HashSet<uint> Weathers { get; set; } = [];

    // Characters by content id: empty means every character.
    public Dictionary<ulong, string> Characters { get; set; } = [];

    // Run after the plate, one per line (e.g. /visor, /facewear). Only lines
    // starting with / are run, so nothing gets said in chat by accident.
    public string ExtraCommands { get; set; } = string.Empty;

    public Rule Clone()
    {
        var copy = (Rule)MemberwiseClone();
        copy.Id = Guid.NewGuid();
        copy.Name = $"{Name} (copy)";
        copy.Jobs = [.. Jobs];
        copy.Regions = [.. Regions];
        copy.ZoneGroups = [.. ZoneGroups];
        copy.Territories = [.. Territories];
        copy.Areas = [.. Areas];
        copy.Weathers = [.. Weathers];
        copy.Characters = new Dictionary<ulong, string>(Characters);
        return copy;
    }

    public bool HasLocations => Regions.Count + ZoneGroups.Count + Territories.Count + Areas.Count > 0;
}

[Serializable]
public sealed class Keybind
{
    public int Key { get; set; }
    public bool Ctrl { get; set; }
    public bool Alt { get; set; }
    public bool Shift { get; set; }

    public override string ToString()
    {
        if (Key == 0) return "Not set";
        var parts = new List<string>();
        if (Ctrl) parts.Add("Ctrl");
        if (Alt) parts.Add("Alt");
        if (Shift) parts.Add("Shift");
        parts.Add(((Dalamud.Game.ClientState.Keys.VirtualKey)Key).ToString().Replace("KEY_", ""));
        return string.Join("+", parts);
    }
}
