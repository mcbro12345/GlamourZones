using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using GlamourZones.Game;

namespace GlamourZones.UI;

// Locations are picked from a tree sorted by expansion: expansion > region >
// city or zone group > single zone. Ticking a region or group covers
// everything under it, including places added in later patches.
public sealed partial class MainWindow
{
    private string placeSearch = string.Empty;

    private bool DrawLocations(Rule rule)
    {
        var count = rule.Regions.Count + rule.ZoneGroups.Count + rule.Territories.Count + rule.Areas.Count;
        if (!ImGui.CollapsingHeader(Header("Locations", count), ImGuiTreeNodeFlags.DefaultOpen))
            return false;
        var changed = false;

        var exclude = rule.ExcludeLocations;
        if (ImGui.Checkbox("Anywhere except these", ref exclude))
        {
            rule.ExcludeLocations = exclude;
            changed = true;
        }
        Hint("No locations picked means anywhere. Tick a whole region (like Coerthas), a city with all its districts, inns and housing (like Ishgard), a single zone, or an area shown under the minimap (like The Jeweled Crozier).\n\nPlates can only go on in resting areas (cities, inns, housing, settlements), so in open zones the plate is applied when you reach one.");

        changed |= DrawCurrentPlaceButtons(rule);
        changed |= DrawChosenPlaces(rule);

        ImGui.SetNextItemWidth(220 * ImGuiHelpers.GlobalScale);
        ImGui.InputTextWithHint("##placeSearch", "Search places", ref placeSearch, 60);
        ImGui.SameLine();
        ImGui.TextColored(Muted, "Sorted by expansion");

        using (var tree = ImRaii.Child("##placeTree", new Vector2(-1, 260 * ImGuiHelpers.GlobalScale), true))
        {
            if (tree)
                changed |= placeSearch.Length > 0 ? DrawPlaceSearch(rule) : DrawPlaceTree(rule);
        }
        changed |= DrawKnownAreas(rule);
        ImGui.Spacing();
        return changed;
    }

    private bool DrawCurrentPlaceButtons(Rule rule)
    {
        if (applier.Now is not { } now)
            return false;
        var changed = false;
        ImGui.TextUnformatted("Add where I am:");
        ImGui.SameLine();
        if (now.Region != 0 && ImGui.SmallButton($"Region: {GameInfo.PlaceName(now.Region)}"))
            changed |= rule.Regions.Add(now.Region);
        if (now.ZoneGroup != 0 && now.ZoneGroup != now.Region)
        {
            ImGui.SameLine();
            if (ImGui.SmallButton($"City/group: {GameInfo.PlaceName(now.ZoneGroup)}"))
                changed |= rule.ZoneGroups.Add(now.ZoneGroup);
        }
        ImGui.SameLine();
        if (ImGui.SmallButton($"Zone: {GameInfo.TerritoryName(now.Territory)}"))
            changed |= rule.Territories.Add(now.Territory);
        if (now.Area != 0)
        {
            ImGui.SameLine();
            if (ImGui.SmallButton($"Area: {GameInfo.PlaceName(now.Area)}"))
                changed |= rule.Areas.Add(now.Area);
        }
        if (now.SubArea != 0)
        {
            ImGui.SameLine();
            if (ImGui.SmallButton($"Sub-area: {GameInfo.PlaceName(now.SubArea)}"))
                changed |= rule.Areas.Add(now.SubArea);
        }
        return changed;
    }

    // The picked places as removable chips.
    private bool DrawChosenPlaces(Rule rule)
    {
        var chips = rule.Regions.Select(id => ("Region", id, rule.Regions))
            .Concat(rule.ZoneGroups.Select(id => ("City/group", id, rule.ZoneGroups)))
            .Concat(rule.Territories.Select(id => ("Zone", id, rule.Territories)))
            .Concat(rule.Areas.Select(id => ("Area", id, rule.Areas)))
            .ToList();
        if (chips.Count == 0)
        {
            ImGui.TextColored(Muted, rule.ExcludeLocations ? "Nothing excluded: matches anywhere." : "Anywhere.");
            return false;
        }

        HashSet<uint>? removeFrom = null;
        uint removeId = 0;
        var right = ImGui.GetWindowPos().X + ImGui.GetWindowContentRegionMax().X;
        for (var i = 0; i < chips.Count; i++)
        {
            var (kind, id, set) = chips[i];
            var name = kind == "Zone" ? GameInfo.TerritoryName(id) : GameInfo.PlaceName(id);
            var label = $"{name}  x##chip{kind}{id}";
            var width = ImGui.CalcTextSize(label.Split("##")[0]).X + ImGui.GetStyle().FramePadding.X * 2;
            if (i > 0)
            {
                ImGui.SameLine();
                if (ImGui.GetItemRectMax().X + ImGui.GetStyle().ItemSpacing.X + width > right)
                    ImGui.NewLine();
            }
            using (ImRaii.PushColor(ImGuiCol.Button, rule.ExcludeLocations ? new Vector4(0.55f, 0.25f, 0.25f, 0.8f) : new Vector4(0.25f, 0.45f, 0.3f, 0.8f)))
            {
                if (ImGui.SmallButton(label))
                    (removeFrom, removeId) = (set, id);
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip($"{kind}: {name}\nClick to remove");
        }
        if (removeFrom == null)
            return false;
        removeFrom.Remove(removeId);
        return true;
    }

    private bool DrawPlaceTree(Rule rule)
    {
        var changed = false;
        foreach (var expansion in GameInfo.Expansions)
        {
            var picked = expansion.Regions.Sum(r => (rule.Regions.Contains(r.Id) ? 1 : 0)
                + r.ZoneGroups.Sum(z => (rule.ZoneGroups.Contains(z.Id) ? 1 : 0) + z.Places.Count(p => p.TerritoryIds.Any(rule.Territories.Contains))));
            using var node = ImRaii.TreeNode(picked > 0 ? $"{expansion.Name} ({picked})###{expansion.Name}" : $"{expansion.Name}###{expansion.Name}");
            if (!node)
                continue;
            foreach (var region in expansion.Regions)
            {
                using var regionId = ImRaii.PushId($"{expansion.Name}{region.Id}");
                changed |= Toggle(rule.Regions, region.Id, false, "Whole region, in every expansion");
                ImGui.SameLine();
                using var regionNode = ImRaii.TreeNode($"{region.Name}###r");
                if (!regionNode)
                    continue;
                var regionOn = rule.Regions.Contains(region.Id);
                foreach (var group in region.ZoneGroups)
                {
                    using var groupId = ImRaii.PushId((int)group.Id);
                    var single = group.Places.Count == 1 && group.Places[0].Name == group.Name;
                    if (single)
                    {
                        changed |= PlaceToggle(rule, group.Places[0], regionOn);
                        ImGui.SameLine();
                        ImGui.TextUnformatted(group.Places[0].Name);
                        continue;
                    }
                    changed |= Toggle(rule.ZoneGroups, group.Id, regionOn, $"Everything in {group.Name}: {string.Join(", ", group.Places.Select(p => p.Name))}");
                    ImGui.SameLine();
                    using var groupNode = ImRaii.TreeNode($"{group.Name}###g");
                    if (!groupNode)
                        continue;
                    var groupOn = regionOn || rule.ZoneGroups.Contains(group.Id);
                    foreach (var place in group.Places)
                    {
                        changed |= PlaceToggle(rule, place, groupOn);
                        ImGui.SameLine();
                        ImGui.TextUnformatted(place.Name);
                    }
                }
            }
        }
        return changed;
    }

    private bool DrawPlaceSearch(Rule rule)
    {
        var changed = false;
        var shown = 0;
        foreach (var expansion in GameInfo.Expansions)
        {
            foreach (var region in expansion.Regions)
            {
                if (Hit(region.Name) && ShowOnce(("r", region.Id)))
                {
                    using var id = ImRaii.PushId($"sr{region.Id}");
                    changed |= Toggle(rule.Regions, region.Id, false, null);
                    ImGui.SameLine();
                    ImGui.TextUnformatted($"{region.Name}");
                    ImGui.SameLine();
                    ImGui.TextColored(Muted, "region");
                    shown++;
                }
                foreach (var group in region.ZoneGroups)
                {
                    var single = group.Places.Count == 1 && group.Places[0].Name == group.Name;
                    if (!single && Hit(group.Name) && ShowOnce(("g", group.Id)))
                    {
                        using var id = ImRaii.PushId($"sg{group.Id}");
                        changed |= Toggle(rule.ZoneGroups, group.Id, rule.Regions.Contains(region.Id), null);
                        ImGui.SameLine();
                        ImGui.TextUnformatted(group.Name);
                        ImGui.SameLine();
                        ImGui.TextColored(Muted, $"city/group in {region.Name}, {expansion.Name}");
                        shown++;
                    }
                    foreach (var place in group.Places.Where(p => Hit(p.Name)))
                    {
                        using var id = ImRaii.PushId($"sp{place.TerritoryIds[0]}");
                        changed |= PlaceToggle(rule, place, rule.Regions.Contains(region.Id) || rule.ZoneGroups.Contains(group.Id));
                        ImGui.SameLine();
                        ImGui.TextUnformatted(place.Name);
                        ImGui.SameLine();
                        ImGui.TextColored(Muted, $"{group.Name}, {expansion.Name}");
                        shown++;
                    }
                }
            }
        }
        seen.Clear();
        if (shown == 0)
            ImGui.TextColored(Muted, "Nothing found. Areas inside a zone are listed below once you've visited them.");
        return changed;
    }

    private readonly HashSet<(string, uint)> seen = [];

    private bool ShowOnce((string, uint) key) => seen.Add(key);

    private bool Hit(string name) => name.Contains(placeSearch, StringComparison.OrdinalIgnoreCase);

    // A checkbox for one id; shown ticked and locked when a parent covers it.
    private static bool Toggle(HashSet<uint> set, uint id, bool coveredByParent, string? tooltip)
    {
        if (coveredByParent)
        {
            using (ImRaii.Disabled())
            {
                var on = true;
                ImGui.Checkbox("##t", ref on);
            }
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip("Already included by a region or city ticked above");
            return false;
        }
        var value = set.Contains(id);
        var changed = ImGui.Checkbox("##t", ref value);
        if (tooltip != null && ImGui.IsItemHovered())
            ImGui.SetTooltip(tooltip);
        if (!changed)
            return false;
        if (value)
            set.Add(id);
        else
            set.Remove(id);
        return true;
    }

    private static bool PlaceToggle(Rule rule, Place place, bool coveredByParent)
    {
        using var id = ImRaii.PushId((int)place.TerritoryIds[0]);
        if (coveredByParent)
            return Toggle(rule.Territories, place.TerritoryIds[0], true, null);
        var on = place.TerritoryIds.Any(rule.Territories.Contains);
        if (!ImGui.Checkbox("##p", ref on))
            return false;
        foreach (var territory in place.TerritoryIds)
        {
            if (on)
                rule.Territories.Add(territory);
            else
                rule.Territories.Remove(territory);
        }
        return true;
    }

    // Areas under the minimap for zones visited so far.
    private bool DrawKnownAreas(Rule rule)
    {
        using var node = ImRaii.TreeNode($"Areas inside zones ({rule.Areas.Count})###areas");
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Areas (the names shown under the minimap) are learned as you walk around.\nOnly zones you've visited with the plugin on are listed.");
        if (!node)
            return false;
        var changed = false;
        var zones = config.KnownAreas
            .Where(z => z.Value.Count > 0)
            .Select(z => (Territory: z.Key, Name: GameInfo.TerritoryName(z.Key), Areas: z.Value))
            .Where(z => placeSearch.Length == 0 || Hit(z.Name) || z.Areas.Any(a => Hit(GameInfo.PlaceName(a))))
            .OrderBy(z => z.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (zones.Count == 0)
            ImGui.TextColored(Muted, "None yet. Walk around with the plugin on and areas will show up here.");
        foreach (var zone in zones)
        {
            using var zoneNode = ImRaii.TreeNode($"{zone.Name}###z{zone.Territory}");
            if (!zoneNode)
                continue;
            foreach (var area in zone.Areas.OrderBy(GameInfo.PlaceName))
            {
                using var id = ImRaii.PushId((int)area);
                changed |= Toggle(rule.Areas, area, false, null);
                ImGui.SameLine();
                ImGui.TextUnformatted(GameInfo.PlaceName(area));
            }
            if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Eraser, "Forget these areas"))
            {
                config.KnownAreas.Remove(zone.Territory);
                changed = true;
            }
        }
        return changed;
    }
}
