using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using GlamourZones.Game;
using Newtonsoft.Json;

namespace GlamourZones.UI;

public sealed partial class MainWindow
{
    private const string SharePrefix = "GZ1:";
    private string importMessage = string.Empty;

    private void DrawPlates()
    {
        ImGui.TextWrapped("Give your plates names so rules are easier to read. The game doesn't name plates itself.");
        if (!Plates.Loaded)
            ImGui.TextColored(Warn, "Open your glamour plates in game once (Glamour Dresser or the gear set list) to see what's on each plate.");
        ImGui.Spacing();

        using var table = ImRaii.Table("##plates", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.ScrollY);
        if (!table)
            return;
        ImGui.TableSetupColumn("Plate", ImGuiTableColumnFlags.WidthFixed, 50 * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthFixed, 200 * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Used by", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("##apply", ImGuiTableColumnFlags.WidthFixed, 70 * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableHeadersRow();

        for (var plate = 1; plate <= Plates.Count; plate++)
        {
            using var id = ImRaii.PushId(plate);
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(plate.ToString());
            if (ImGui.IsItemHovered())
                PlateTooltip(plate);

            ImGui.TableNextColumn();
            var name = config.PlateNames.GetValueOrDefault(plate, string.Empty);
            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputTextWithHint("##name", "e.g. Ishgard winter coat", ref name, 40))
            {
                if (string.IsNullOrWhiteSpace(name))
                    config.PlateNames.Remove(plate);
                else
                    config.PlateNames[plate] = name;
                config.Save();
            }

            ImGui.TableNextColumn();
            var users = config.Rules.Where(r => r.Plate == plate).Select(r => r.Name).ToList();
            if (config.FallbackPlate == plate)
                users.Add("default fallback");
            users.AddRange(config.JobFallbacks.Where(j => j.Value == plate).Select(j => $"{GameInfo.Job(j.Key)?.Abbreviation} fallback"));
            ImGui.AlignTextToFramePadding();
            if (users.Count > 0)
                ImGui.TextUnformatted(string.Join(", ", users));
            else
                ImGui.TextColored(Muted, Plates.Loaded ? string.Join(", ", Plates.Items(plate).Take(3)) : "-");

            ImGui.TableNextColumn();
            if (ImGui.SmallButton("Apply") && Plates.Apply(Plates.GearsetFor(applier.Now?.Job ?? 0, config.UseJobGearset), plate) is { } error)
                Services.Chat.PrintError($"Couldn't apply plate {plate}: {error}.", "Glamour Zones");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Put this plate on now (only works in a resting area)");
        }
    }

    private void DrawStatus()
    {
        var now = applier.Now;
        if (now == null)
        {
            ImGui.TextColored(Muted, "Log in to see your current status.");
            return;
        }

        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Tshirt, "Apply now"))
            applier.ApplyNow();
        ImGui.SameLine();
        if (applier.Paused)
        {
            if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Play, "Resume"))
                applier.Resume();
        }
        else if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Pause, "Pause until next zone"))
        {
            applier.PauseHere();
        }
        ImGui.Spacing();

        using var table = ImRaii.Table("##status", 2, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH);
        if (!table)
            return;
        ImGui.TableSetupColumn("##k", ImGuiTableColumnFlags.WidthFixed, 160 * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("##v", ImGuiTableColumnFlags.WidthStretch);

        Row("Character", now.Character);
        Row("Job", GameInfo.Job(now.Job)?.Name ?? $"#{now.Job}");
        Row("Region", GameInfo.PlaceName(now.Region));
        Row("City / zone group", GameInfo.PlaceName(now.ZoneGroup));
        Row("Zone", $"{GameInfo.TerritoryName(now.Territory)} (#{now.Territory})");
        Row("Area", GameInfo.PlaceName(now.Area));
        Row("Sub-area", GameInfo.PlaceName(now.SubArea));
        Row("Weather", now.Weather == 0 ? "(none)" : GameInfo.WeatherName(now.Weather));
        Row("Eorzea time", $"{now.EorzeaHour:00}:{now.EorzeaMinute:00}");
        Row("Resting area", now.InRestingArea ? "Yes" : "No", now.InRestingArea ? Good : Warn);
        Row("Game allows plates", now.GameAllowsPlates ? "Yes" : "No", now.GameAllowsPlates ? Good : Warn);
        var houses = HouseKinds.Where(k => now.Houses.HasFlag(k.Kind)).Select(k => k.Label).ToList();
        Row("Inside", houses.Count == 0 ? "(not in a house)" : string.Join(", ", houses));
        var gearset = Plates.GearsetFor(now.Job, config.UseJobGearset);
        Row("Gear set", gearset < 0 ? "none worn or saved for this job" : Plates.IsWearingGearset ? Plates.GearsetName(gearset) : $"{Plates.GearsetName(gearset)} (not worn, will be put on)",
            gearset < 0 ? Warn : (Vector4?)null);
        if (GlamourerLoaded)
            Row("Glamourer", "running: its designs can cover plates", Warn);
        Row("Matching rule", applier.Current?.Name ?? "none, using the fallback");
        Row("Plate", Plates.Label(config, applier.CurrentPlate));
        Row("Pending", applier.Pending ? $"yes ({applier.PendingReason}){(applier.Waiting.Length > 0 ? $", waiting: {applier.Waiting}" : string.Empty)}" : "no",
            applier.Pending ? Warn : Muted);
        Row("Last applied", applier.LastApplied);
    }

    private static void Row(string key, string value, Vector4? color = null)
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.TextColored(Muted, key);
        ImGui.TableNextColumn();
        if (color is { } c)
            ImGui.TextColored(c, value);
        else
            ImGui.TextUnformatted(value);
    }

    private void DrawSettings()
    {
        using var scroll = ImRaii.Child("##settings", new Vector2(-1, -1), false);
        if (!scroll)
            return;
        var changed = false;
        ImGui.TextUnformatted("Fallbacks");
        ImGui.SetNextItemWidth(260 * ImGuiHelpers.GlobalScale);
        var fallback = PlateCombo("Default fallback", config.FallbackPlate, true);
        if (fallback != config.FallbackPlate)
        {
            config.FallbackPlate = fallback;
            changed = true;
        }
        Hint("Used when no rule matches. \"Leave as is\" does nothing; \"Gear set's linked plate\" puts back the plate linked to your gear set, a handy \"back to normal\" when you leave a rule's area.");
        changed |= DrawJobFallbacks();

        ImGui.Spacing();
        ImGui.TextUnformatted("Timing");
        ImGui.SetNextItemWidth(260 * ImGuiHelpers.GlobalScale);
        var delay = config.DelaySeconds;
        if (ImGui.SliderFloat("Delay after a change", ref delay, 0f, 10f, "%.1f s"))
        {
            config.DelaySeconds = delay;
            changed = true;
        }
        Hint("How long to wait after changing zone, job or conditions before applying. A short delay lets the game settle after loading.");

        ImGui.Spacing();
        ImGui.TextUnformatted("When to apply");
        changed |= Check("Every time I change zone", config.ReapplyOnZoneChange, v => config.ReapplyOnZoneChange = v,
            "Re-applies even if the same rule matched before, so changes you made by hand get replaced when you move on.");
        changed |= Check("Every time I change job", config.ReapplyOnJobChange, v => config.ReapplyOnJobChange = v,
            "Changing job swaps your gear, so this puts the plate back on the new gear. Also overrides plates linked to gear sets.");
        changed |= Check("When the area, time or weather changes", config.ReactInsideZone, v => config.ReactInsideZone = v,
            "Checks rules while you stay in a zone, e.g. walking into another area or night falling.");
        changed |= Check("Wait until I reach a resting area", config.WaitForRestingArea, v => config.WaitForRestingArea = v,
            "Plates can only go on in resting areas. With this on, the plate is applied as soon as you enter one in the same zone. With it off, the change is skipped.");
        changed |= Check("Wait until out of combat", config.SkipInCombat, v => config.SkipInCombat = v, null);
        changed |= Check("Don't re-apply a plate that's already on", config.SkipIfAlreadyOn, v => config.SkipIfAlreadyOn = v,
            "Applying re-equips your gear set, which undoes gear you swapped by hand. With this on, the plate is skipped when you're still wearing exactly what it put on. \"Apply now\" always applies.");

        ImGui.Spacing();
        ImGui.TextUnformatted("Gear sets");
        changed |= Check("If I'm not wearing a gear set, use the first one saved for my job", config.UseJobGearset, v => config.UseJobGearset = v,
            "Plates go on through a gear set. With this off, nothing is applied until you put on a saved gear set.");

        ImGui.Spacing();
        ImGui.TextUnformatted("Feedback");
        changed |= Check("Say in chat when a plate is applied", config.ChatNotify, v => config.ChatNotify = v, null);
        changed |= Check("Show in the server info bar", config.ShowDtr, v => config.ShowDtr = v,
            "Shows the current plate. Click it to turn the plugin on or off.");
        if (GlamourerLoaded)
            changed |= Check("Show the Glamourer notice", !config.GlamourerNoticeDismissed, v => config.GlamourerNoticeDismissed = !v, null);

        ImGui.Spacing();
        ImGui.TextUnformatted("Keybinds");
        KeybindRow("Turn automatic plates on/off", config.ToggleKey);
        KeybindRow("Apply the matching plate now", config.ApplyKey);
        ImGui.TextDisabled("Keybinds are ignored while typing.");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextUnformatted("Share rules");
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.FileExport, "Copy all rules"))
            ImGui.SetClipboardText(Export(config.Rules));
        ImGui.SameLine();
        using (ImRaii.Disabled(SelectedRule == null))
        {
            if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Copy, "Copy selected rule") && SelectedRule is { } rule)
                ImGui.SetClipboardText(Export([rule]));
        }
        ImGui.SameLine();
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.FileImport, "Import from clipboard"))
            importMessage = Import(ImGui.GetClipboardText());
        if (importMessage.Length > 0)
            ImGui.TextColored(Muted, importMessage);

        if (changed)
            config.Save();
    }

    // A fallback per job, grouped by role; jobs left on "Default fallback"
    // use the default above.
    private bool DrawJobFallbacks()
    {
        var custom = config.JobFallbacks.Count(j => j.Value != Configuration.UseDefault);
        using var node = ImRaii.TreeNode(custom == 0 ? "Per-job fallbacks###jobFallbacks" : $"Per-job fallbacks ({custom} set)###jobFallbacks");
        if (!node)
            return false;
        var changed = false;
        using var table = ImRaii.Table("##jobFallbacks", 2, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH);
        if (!table)
            return false;
        ImGui.TableSetupColumn("##job", ImGuiTableColumnFlags.WidthFixed, 170 * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("##plate", ImGuiTableColumnFlags.WidthStretch);
        var iconSize = new Vector2(ImGui.GetFrameHeight());
        foreach (var job in GameInfo.Jobs.Where(j => !j.IsBaseClass || config.JobFallbacks.ContainsKey(j.Id)))
        {
            using var id = ImRaii.PushId((int)job.Id);
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Image(Services.Textures.GetFromGameIcon(new GameIconLookup(job.Icon)).GetWrapOrEmpty().Handle, iconSize);
            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(job.Name);
            ImGui.TableNextColumn();
            var current = config.JobFallbacks.GetValueOrDefault(job.Id, Configuration.UseDefault);
            ImGui.SetNextItemWidth(260 * ImGuiHelpers.GlobalScale);
            var plate = PlateCombo("##fallback", current, true, allowDefault: true);
            if (plate == current)
                continue;
            if (plate == Configuration.UseDefault)
                config.JobFallbacks.Remove(job.Id);
            else
                config.JobFallbacks[job.Id] = plate;
            changed = true;
        }
        return changed;
    }

    private static bool Check(string label, bool value, Action<bool> set, string? hint)
    {
        var changed = ImGui.Checkbox(label, ref value);
        if (changed)
            set(value);
        if (hint != null)
            Hint(hint);
        return changed;
    }

    private static string Export(IEnumerable<Rule> rules)
    {
        // Characters are left out; they only mean something on this account.
        var copies = rules.Select(r =>
        {
            var copy = r.Clone();
            copy.Name = r.Name;
            copy.Characters.Clear();
            return copy;
        }).ToList();
        var json = JsonConvert.SerializeObject(copies);
        return SharePrefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    private string Import(string? text)
    {
        text = text?.Trim() ?? string.Empty;
        if (!text.StartsWith(SharePrefix))
            return "The clipboard doesn't hold Glamour Zones rules.";
        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(text[SharePrefix.Length..]));
            var rules = JsonConvert.DeserializeObject<List<Rule>>(json) ?? [];
            foreach (var rule in rules)
            {
                rule.Id = Guid.NewGuid();
                rule.Plate = Math.Clamp(rule.Plate, 1, Plates.Count);
                config.Rules.Add(rule);
            }
            config.Save();
            if (rules.Count > 0)
                selected = rules[0].Id;
            var withCommands = rules.Count(r => r.ExtraCommands.Split('\n').Any(l => l.Trim().StartsWith('/')));
            var message = $"Imported {rules.Count} rule(s) at the bottom of the list.";
            return withCommands == 0 ? message : $"{message} {withCommands} of them run extra commands: check those before relying on them.";
        }
        catch (Exception e)
        {
            Services.Log.Warning(e, "Rule import failed");
            return "Couldn't read those rules.";
        }
    }
}
