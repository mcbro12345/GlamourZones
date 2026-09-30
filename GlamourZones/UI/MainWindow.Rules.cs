using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using GlamourZones.Game;

namespace GlamourZones.UI;

public sealed partial class MainWindow
{
    private void DrawRules()
    {
        var listWidth = 250 * ImGuiHelpers.GlobalScale;
        using (var list = ImRaii.Child("##ruleList", new Vector2(listWidth, -1), true))
        {
            if (list)
                DrawRuleList();
        }
        ImGui.SameLine();
        using var editor = ImRaii.Child("##ruleEditor", new Vector2(-1, -1), true);
        if (!editor)
            return;
        var rule = SelectedRule;
        if (rule == null)
        {
            DrawWelcome();
            return;
        }
        DrawRuleEditor(rule);
    }

    private void DrawRuleList()
    {
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Plus, "New"))
            AddRule(new Rule { Name = $"Rule {config.Rules.Count + 1}" });
        ImGui.SameLine();
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.MapMarkerAlt, "Here"))
            AddRule(RuleForHere());
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("New rule for your current job and city or zone.");
        DrawGroupToggles();
        ImGui.Separator();

        for (var i = 0; i < config.Rules.Count; i++)
        {
            var rule = config.Rules[i];
            using var id = ImRaii.PushId(rule.Id.ToString());
            var enabled = rule.Enabled;
            if (ImGui.Checkbox("##on", ref enabled))
            {
                rule.Enabled = enabled;
                config.Save();
            }
            ImGui.SameLine();
            var active = applier.Current?.Id == rule.Id;
            using (ImRaii.PushColor(ImGuiCol.Text, Good, active))
            using (ImRaii.PushColor(ImGuiCol.Text, Muted, !active && !config.IsActive(rule)))
            {
                var group = rule.Group.Length > 0 ? $"  [{rule.Group}]" : string.Empty;
                if (ImGui.Selectable($"{i + 1}. {rule.Name}{group}", selected == rule.Id))
                    selected = rule.Id;
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip($"{Plates.Label(config, rule.Plate)}\n{Summary(rule)}{(active ? "\n\nMatching right now" : string.Empty)}\n\nDrag to reorder");

            // Drag a rule onto another to move it there.
            using (var source = ImRaii.DragDropSource())
            {
                if (source)
                {
                    ImGui.SetDragDropPayload("GZRule", ReadOnlySpan<byte>.Empty, ImGuiCond.None);
                    dragging = i;
                    ImGui.TextUnformatted(rule.Name);
                }
            }
            using (var target = ImRaii.DragDropTarget())
            {
                if ((bool)target && !ImGui.AcceptDragDropPayload("GZRule").IsNull && dragging >= 0 && dragging != i)
                {
                    var moved = config.Rules[dragging];
                    config.Rules.RemoveAt(dragging);
                    config.Rules.Insert(i, moved);
                    dragging = -1;
                    config.Save();
                }
            }
        }
    }

    private int dragging = -1;

    private void AddRule(Rule rule)
    {
        config.Rules.Add(rule);
        selected = rule.Id;
        config.Save();
    }

    private Rule RuleForHere()
    {
        var rule = new Rule();
        var now = applier.Now;
        if (now == null)
            return rule;
        rule.Jobs.Add(now.Job);
        if (now.ZoneGroup != 0 && now.ZoneGroup != now.Region)
            rule.ZoneGroups.Add(now.ZoneGroup);
        else
            rule.Territories.Add(now.Territory);
        var job = GameInfo.Job(now.Job)?.Abbreviation ?? "Job";
        var place = rule.ZoneGroups.Count > 0 ? GameInfo.PlaceName(now.ZoneGroup) : GameInfo.TerritoryName(now.Territory);
        rule.Name = $"{job} in {place}";
        return rule;
    }

    private static string Summary(Rule rule)
    {
        var jobs = rule.Jobs.Count == 0 ? "Any job" : string.Join(", ", rule.Jobs.Select(j => GameInfo.Job(j)?.Abbreviation ?? $"#{j}"));
        var count = rule.Regions.Count + rule.ZoneGroups.Count + rule.Territories.Count + rule.Areas.Count;
        var places = count == 0 ? "anywhere" : rule.ExcludeLocations ? $"outside {count} place(s)" : $"in {count} place(s)";
        var extra = rule.UseTime ? $", ET {rule.TimeStart:00}:00-{rule.TimeEnd:00}:00" : string.Empty;
        if (rule.Weathers.Count > 0)
            extra += $", {rule.Weathers.Count} weather(s)";
        return $"{jobs}, {places}{extra}";
    }

    private void DrawRuleEditor(Rule rule)
    {
        var changed = false;

        ImGui.SetNextItemWidth(260 * ImGuiHelpers.GlobalScale);
        var name = rule.Name;
        if (ImGui.InputText("Name", ref name, 64))
        {
            rule.Name = name;
            changed = true;
        }
        ImGui.SameLine();
        var index = config.Rules.IndexOf(rule);
        using (ImRaii.Disabled(index <= 0))
        {
            if (ImGuiComponents.IconButton(FontAwesomeIcon.ArrowUp))
                Move(rule, index - 1);
        }
        ImGui.SameLine();
        using (ImRaii.Disabled(index >= config.Rules.Count - 1))
        {
            if (ImGuiComponents.IconButton(FontAwesomeIcon.ArrowDown))
                Move(rule, index + 1);
        }
        ImGui.SameLine();
        if (ImGuiComponents.IconButton(FontAwesomeIcon.Copy))
            AddRule(rule.Clone());
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Duplicate");
        ImGui.SameLine();
        using (ImRaii.Disabled(!ImGui.GetIO().KeyCtrl))
        {
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Trash))
            {
                config.Rules.Remove(rule);
                selected = config.Rules.ElementAtOrDefault(Math.Max(0, index - 1))?.Id ?? Guid.Empty;
                config.Save();
                return;
            }
        }
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip("Hold Ctrl and click to delete");

        ImGui.SetNextItemWidth(260 * ImGuiHelpers.GlobalScale);
        changed |= GroupPicker(rule);
        ImGui.SetNextItemWidth(260 * ImGuiHelpers.GlobalScale);
        var plate = PlateCombo("Glamour plate", rule.Plate, false);
        if (plate != rule.Plate)
        {
            rule.Plate = plate;
            changed = true;
        }
        ImGui.SameLine();
        var enabled = rule.Enabled;
        if (ImGui.Checkbox("Rule enabled", ref enabled))
        {
            rule.Enabled = enabled;
            changed = true;
        }
        if (applier.Now is { } now)
        {
            ImGui.SameLine();
            if (config.DisabledGroups.Contains(rule.Group))
                ImGui.TextColored(Muted, $"Group \"{rule.Group}\" is switched off");
            else if (!rule.Enabled)
                ImGui.TextColored(Muted, "Disabled");
            else if (rule.Matches(now))
                ImGui.TextColored(Good, applier.Current?.Id == rule.Id ? "Matches now" : "Matches now (a rule above wins)");
            else
                ImGui.TextColored(Muted, "Doesn't match right now");
        }

        ImGui.Spacing();
        changed |= DrawJobs(rule);
        changed |= DrawLocations(rule);
        changed |= DrawHousing(rule);
        changed |= DrawTimeAndWeather(rule);
        changed |= DrawCharacters(rule);
        changed |= DrawExtraCommands(rule);

        if (changed)
            config.Save();
    }

    private void Move(Rule rule, int to)
    {
        config.Rules.Remove(rule);
        config.Rules.Insert(Math.Clamp(to, 0, config.Rules.Count), rule);
        config.Save();
    }

    private static string Header(string title, int count) => count == 0 ? $"{title} (any)###{title}" : $"{title} ({count})###{title}";

    private bool DrawJobs(Rule rule)
    {
        if (!ImGui.CollapsingHeader(Header("Jobs", rule.Jobs.Count), ImGuiTreeNodeFlags.DefaultOpen))
            return false;
        var changed = false;
        if (ImGui.SmallButton("Current job##jobs") && applier.Now is { } now)
            changed |= rule.Jobs.Add(now.Job);
        ImGui.SameLine();
        if (ImGui.SmallButton("All jobs##jobs"))
        {
            foreach (var job in GameInfo.Jobs.Where(j => !j.IsBaseClass))
                rule.Jobs.Add(job.Id);
            changed = true;
        }
        ImGui.SameLine();
        if (ImGui.SmallButton("Clear##jobs"))
        {
            rule.Jobs.Clear();
            changed = true;
        }
        ImGui.SameLine();
        showBaseClasses = showBaseClasses || rule.Jobs.Any(j => GameInfo.Job(j)?.IsBaseClass == true);
        ImGui.Checkbox("Show base classes", ref showBaseClasses);
        Hint("No jobs picked means the rule works on every job. Click a role name to pick or clear that whole role.");

        var size = new Vector2(34, 34) * ImGuiHelpers.GlobalScale;
        foreach (var group in GameInfo.Jobs.Where(j => showBaseClasses || !j.IsBaseClass).GroupBy(j => j.Group))
        {
            var label = group.Key switch
            {
                JobGroup.PhysicalRanged => "Ranged",
                JobGroup.MagicalRanged => "Casters",
                JobGroup.Tank => "Tanks",
                JobGroup.Healer => "Healers",
                JobGroup.Crafter => "Crafters",
                JobGroup.Gatherer => "Gatherers",
                _ => group.Key.ToString(),
            };
            var ids = group.Select(j => j.Id).ToList();
            var all = ids.All(rule.Jobs.Contains);
            if (ImGui.Selectable($"{label}##{group.Key}", all, ImGuiSelectableFlags.None, new Vector2(70 * ImGuiHelpers.GlobalScale, size.Y)))
            {
                foreach (var id in ids)
                {
                    if (all)
                        rule.Jobs.Remove(id);
                    else
                        rule.Jobs.Add(id);
                }
                changed = true;
            }
            foreach (var job in group)
            {
                ImGui.SameLine();
                changed |= JobToggle(rule, job, size);
            }
        }
        ImGui.Spacing();
        return changed;
    }

    private bool showBaseClasses;

    private static bool JobToggle(Rule rule, JobInfo job, Vector2 size)
    {
        var on = rule.Jobs.Contains(job.Id);
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton($"##job{job.Id}", size);
        var hovered = ImGui.IsItemHovered();
        var draw = ImGui.GetWindowDrawList();
        var icon = Services.Textures.GetFromGameIcon(new GameIconLookup(job.Icon)).GetWrapOrEmpty();
        var tint = on ? Vector4.One : hovered ? new Vector4(1, 1, 1, 0.7f) : new Vector4(1, 1, 1, 0.25f);
        draw.AddImage(icon.Handle, pos, pos + size, Vector2.Zero, Vector2.One, ImGui.ColorConvertFloat4ToU32(tint));
        if (on)
            draw.AddRect(pos, pos + size, ImGui.ColorConvertFloat4ToU32(Good), 4f, ImDrawFlags.None, 2f);
        if (hovered)
            ImGui.SetTooltip($"{job.Name} ({job.Abbreviation})");
        if (!clicked)
            return false;
        if (on)
            rule.Jobs.Remove(job.Id);
        else
            rule.Jobs.Add(job.Id);
        return true;
    }

    private bool DrawTimeAndWeather(Rule rule)
    {
        var changed = false;
        var count = (rule.UseTime ? 1 : 0) + rule.Weathers.Count;
        if (ImGui.CollapsingHeader(Header("Time and weather", count)))
        {
            var useTime = rule.UseTime;
            if (ImGui.Checkbox("Only between these Eorzea hours", ref useTime))
            {
                rule.UseTime = useTime;
                changed = true;
            }
            if (applier.Now is { } now)
            {
                ImGui.SameLine();
                ImGui.TextColored(Muted, $"(now {now.EorzeaHour:00}:{now.EorzeaMinute:00} ET)");
            }
            using (ImRaii.Disabled(!rule.UseTime))
            {
                var start = rule.TimeStart;
                var end = rule.TimeEnd;
                ImGui.SetNextItemWidth(160 * ImGuiHelpers.GlobalScale);
                if (ImGui.SliderInt("From##time", ref start, 0, 23, "%02d:00"))
                {
                    rule.TimeStart = start;
                    changed = true;
                }
                ImGui.SameLine();
                ImGui.SetNextItemWidth(160 * ImGuiHelpers.GlobalScale);
                if (ImGui.SliderInt("Until##time", ref end, 0, 23, "%02d:00"))
                {
                    rule.TimeEnd = end;
                    changed = true;
                }
                ImGui.SameLine();
                if (ImGui.SmallButton("Night"))
                {
                    (rule.TimeStart, rule.TimeEnd) = (18, 6);
                    changed = true;
                }
                ImGui.SameLine();
                if (ImGui.SmallButton("Day"))
                {
                    (rule.TimeStart, rule.TimeEnd) = (6, 18);
                    changed = true;
                }
            }

            ImGui.Spacing();
            ImGui.TextUnformatted("Weather");
            Hint("Pick none to allow any weather. The rule is checked again whenever the weather changes.");
            ImGui.SameLine();
            if (ImGui.SmallButton("Current weather") && applier.Now is { Weather: > 0 } here)
                changed |= rule.Weathers.Add(here.Weather);
            ImGui.SameLine();
            if (ImGui.SmallButton("Clear##weather"))
            {
                rule.Weathers.Clear();
                changed = true;
            }
            ImGui.SetNextItemWidth(200 * ImGuiHelpers.GlobalScale);
            ImGui.InputTextWithHint("##weatherSearch", "Search weather", ref weatherSearch, 40);
            using (var list = ImRaii.Child("##weathers", new Vector2(-1, 150 * ImGuiHelpers.GlobalScale), true))
            {
                if (list)
                {
                    var iconSize = new Vector2(ImGui.GetTextLineHeight());
                    foreach (var weather in GameInfo.Weathers.Where(w => weatherSearch.Length == 0 || w.Name.Contains(weatherSearch, StringComparison.OrdinalIgnoreCase)))
                    {
                        var on = rule.Weathers.Contains(weather.Id);
                        if (ImGui.Checkbox($"##w{weather.Id}", ref on))
                        {
                            if (on)
                                rule.Weathers.Add(weather.Id);
                            else
                                rule.Weathers.Remove(weather.Id);
                            changed = true;
                        }
                        ImGui.SameLine();
                        if (weather.Icon > 0)
                        {
                            ImGui.Image(Services.Textures.GetFromGameIcon(new GameIconLookup(weather.Icon)).GetWrapOrEmpty().Handle, iconSize);
                            ImGui.SameLine();
                        }
                        ImGui.TextUnformatted(weather.Name);
                    }
                }
            }
        }
        return changed;
    }

    private string weatherSearch = string.Empty;

    private bool DrawCharacters(Rule rule)
    {
        var changed = false;
        if (ImGui.CollapsingHeader(Header("Characters", rule.Characters.Count)))
        {
            ImGui.TextColored(Muted, "Leave empty for every character on this account.");
            if (ImGui.SmallButton("Add current character") && applier.Now is { ContentId: > 0 } now)
            {
                rule.Characters[now.ContentId] = now.Character;
                changed = true;
            }
            foreach (var (id, name) in rule.Characters.ToList())
            {
                if (ImGuiComponents.IconButton((int)(id % int.MaxValue), FontAwesomeIcon.Times))
                {
                    rule.Characters.Remove(id);
                    changed = true;
                }
                ImGui.SameLine();
                ImGui.TextUnformatted(name);
            }
        }
        return changed;
    }

    private bool DrawExtraCommands(Rule rule)
    {
        var changed = false;
        var count = rule.ExtraCommands.Split('\n').Count(l => l.Trim().StartsWith('/'));
        if (ImGui.CollapsingHeader(count == 0 ? "Extra commands###Extra" : $"Extra commands ({count})###Extra"))
        {
            ImGui.TextColored(Muted, "Run after the plate goes on, one per line. Only lines starting with / are run.");
            ImGui.TextColored(Muted, "For example /visor, /facewear 3, /gs change 5 or another plugin's command.");
            var text = rule.ExtraCommands;
            if (ImGui.InputTextMultiline("##extra", ref text, 2000, new Vector2(-1, 90 * ImGuiHelpers.GlobalScale)))
            {
                rule.ExtraCommands = text;
                changed = true;
            }
        }
        return changed;
    }
}
