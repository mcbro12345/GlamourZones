using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using GlamourZones.Game;

namespace GlamourZones.UI;

// First-run help, rule groups and housing conditions.
public sealed partial class MainWindow
{
    private string newGroup = string.Empty;

    private void DrawWelcome()
    {
        if (config.Rules.Count > 0)
        {
            ImGui.TextColored(Muted, "Pick a rule on the left to edit it.");
            return;
        }

        ImGui.TextColored(Good, "Welcome to Glamour Zones");
        ImGui.Spacing();
        ImGui.TextWrapped("Glamour Zones puts on a glamour plate for you depending on your job and where you are, like \"Red Mage in Ishgard wears plate 4\".");
        ImGui.Spacing();
        Bullet("Rules are checked from the top of the list down. The first one that matches picks the plate, so put specific rules above general ones.");
        Bullet("A rule can check jobs, places (sorted by expansion), houses, Eorzea time, weather and character. Anything left empty matches everything.");
        Bullet("Plates go on through your gear set, the same as linking a plate in the gear set list, so wear a saved gear set.");
        Bullet("The game only allows plates in resting areas (cities, inns, housing, settlements). In open zones the plate goes on when you reach one.");
        Bullet("When no rule matches, the fallback is used. You can set a different one per job on the Settings tab.");
        ImGui.Spacing();

        if (applier.Now is { } now)
        {
            var example = RuleForHere();
            if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Magic, $"Make my first rule: \"{example.Name}\""))
                AddRule(example);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Creates a rule for your current job here, using plate 1. Change the plate and places afterwards.");
        }
        else
        {
            ImGui.TextColored(Muted, "Log in to make a rule for where you are.");
        }
        ImGui.SameLine();
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Tshirt, "Name my plates"))
            OpenTab("Plates");
        ImGui.SameLine();
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Cog, "Set fallbacks"))
            OpenTab("Settings");
    }

    private static void Bullet(string text)
    {
        ImGui.Bullet();
        ImGui.TextWrapped(text);
    }

    // Groups let whole sets of rules be switched on and off, like a winter
    // set or an event set. Order still comes from the list.
    private void DrawGroupToggles()
    {
        var groups = config.Rules.Select(r => r.Group).Where(g => g.Length > 0).Distinct().OrderBy(g => g, StringComparer.OrdinalIgnoreCase).ToList();
        if (groups.Count == 0)
            return;
        ImGui.TextColored(Muted, "Groups");
        foreach (var group in groups)
        {
            var on = !config.DisabledGroups.Contains(group);
            if (ImGui.Checkbox($"{group}##group", ref on))
            {
                if (on)
                    config.DisabledGroups.Remove(group);
                else
                    config.DisabledGroups.Add(group);
                config.Save();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip($"Switch every rule in \"{group}\" on or off.\nAlso: /gzones group {group} on|off");
        }
    }

    private bool GroupPicker(Rule rule)
    {
        var changed = false;
        using var combo = ImRaii.Combo("Group", rule.Group.Length == 0 ? "(no group)" : rule.Group);
        if (!combo)
            return false;
        if (ImGui.Selectable("(no group)", rule.Group.Length == 0))
        {
            rule.Group = string.Empty;
            changed = true;
        }
        foreach (var group in config.Rules.Select(r => r.Group).Where(g => g.Length > 0).Distinct().OrderBy(g => g, StringComparer.OrdinalIgnoreCase))
        {
            if (ImGui.Selectable(group, rule.Group == group))
            {
                rule.Group = group;
                changed = true;
            }
        }
        ImGui.Separator();
        ImGui.SetNextItemWidth(160 * ImGuiHelpers.GlobalScale);
        var enter = ImGui.InputTextWithHint("##newGroup", "New group name", ref newGroup, 32, ImGuiInputTextFlags.EnterReturnsTrue);
        ImGui.SameLine();
        if ((ImGui.SmallButton("Add") || enter) && newGroup.Trim().Length > 0)
        {
            rule.Group = newGroup.Trim();
            newGroup = string.Empty;
            changed = true;
            ImGui.CloseCurrentPopup();
        }
        return changed;
    }

    private static readonly (HouseKind Kind, string Label, string Tip)[] HouseKinds =
    [
        (HouseKind.MyHouse, "My house", "Inside your own house, or one you share"),
        (HouseKind.MyFreeCompanyHouse, "My Free Company house", "Inside your Free Company's house, including its private rooms"),
        (HouseKind.MyPrivateChambers, "My private chambers", "Your own room in the Free Company house"),
        (HouseKind.MyApartment, "My apartment", "Inside your apartment"),
        (HouseKind.SomeoneElses, "Someone else's home", "Inside any house or apartment that isn't yours"),
    ];

    private bool DrawHousing(Rule rule)
    {
        var count = HouseKinds.Count(k => rule.Houses.HasFlag(k.Kind));
        if (!ImGui.CollapsingHeader(Header("Housing", count)))
            return false;
        ImGui.TextColored(Muted, "Only when inside one of these. Tick none to ignore housing.");
        ImGui.TextColored(Muted, "Works together with Locations: leave those empty to mean any ward.");
        var changed = false;
        foreach (var (kind, label, tip) in HouseKinds)
        {
            var on = rule.Houses.HasFlag(kind);
            if (ImGui.Checkbox(label, ref on))
            {
                rule.Houses = on ? rule.Houses | kind : rule.Houses & ~kind;
                changed = true;
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(tip);
        }
        if (applier.Now is { } now)
        {
            var here = HouseKinds.Where(k => now.Houses.HasFlag(k.Kind)).Select(k => k.Label).ToList();
            ImGui.TextColored(Muted, here.Count == 0 ? "You're not inside a house right now." : $"Right now: {string.Join(", ", here)}");
        }
        ImGui.Spacing();
        return changed;
    }
}
