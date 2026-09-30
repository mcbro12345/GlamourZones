using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using GlamourZones.Game;

namespace GlamourZones.UI;

public sealed partial class MainWindow : Window
{
    private static readonly Vector4 Good = new(0.45f, 0.85f, 0.45f, 1f);
    private static readonly Vector4 Warn = new(0.95f, 0.75f, 0.3f, 1f);
    private static readonly Vector4 Muted = new(0.6f, 0.6f, 0.6f, 1f);

    private readonly Configuration config;
    private readonly AutoApplier applier;
    private Guid selected;

    public MainWindow(Configuration config, AutoApplier applier)
        : base("Glamour Zones###GlamourZonesMain")
    {
        this.config = config;
        this.applier = applier;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(760, 480), MaximumSize = new Vector2(4000, 4000) };
        Size = new Vector2(980, 680);
        SizeCondition = ImGuiCond.FirstUseEver;
        selected = config.Rules.FirstOrDefault()?.Id ?? Guid.Empty;
    }

    public void OpenTab(string tab)
    {
        requestedTab = tab;
        IsOpen = true;
    }

    private string? requestedTab;

    public override void Draw()
    {
        DrawHeader();
        DrawGlamourerNotice();
        using var tabs = ImRaii.TabBar("##tabs");
        if (!tabs)
            return;
        Tab("Rules", DrawRules);
        Tab("Plates", DrawPlates);
        Tab("Status", DrawStatus);
        Tab("Settings", DrawSettings);
        requestedTab = null;
    }

    private void Tab(string name, Action draw)
    {
        var flags = requestedTab == name ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;
        using var tab = ImRaii.TabItem(name, flags);
        if (tab)
            draw();
    }

    private void DrawHeader()
    {
        var enabled = config.Enabled;
        if (ImGui.Checkbox("Enabled", ref enabled))
        {
            config.Enabled = enabled;
            config.Save();
            if (enabled)
                applier.ApplyNow("enabled");
        }
        ImGui.SameLine();
        ImGui.TextColored(Muted, "|");
        ImGui.SameLine();
        var now = applier.Now;
        if (now == null)
        {
            ImGui.TextColored(Muted, "Not logged in");
            return;
        }
        var job = GameInfo.Job(now.Job)?.Abbreviation ?? "?";
        ImGui.TextUnformatted($"{job} in {GameInfo.TerritoryName(now.Territory)}");
        ImGui.SameLine();
        ImGui.TextColored(Muted, "->");
        ImGui.SameLine();
        if (applier.Current != null)
            ImGui.TextColored(Good, $"{applier.Current.Name} ({Plates.Label(config, applier.Current.Plate)})");
        else if (config.FallbackFor(now.Job) != Configuration.LeaveAlone)
            ImGui.TextColored(Good, $"Fallback ({Plates.Label(config, config.FallbackFor(now.Job))})");
        else
            ImGui.TextColored(Muted, "No rule matches");
        if (applier.Pending && applier.Waiting.Length > 0)
        {
            ImGui.SameLine();
            ImGui.TextColored(Warn, $"waiting: {applier.Waiting}");
        }
        else if (applier.Paused)
        {
            ImGui.SameLine();
            ImGui.TextColored(Warn, "paused until next zone");
        }
    }

    private Rule? SelectedRule => config.Rules.FirstOrDefault(r => r.Id == selected);

    // Fallback pickers also offer leaving things alone, the gear set's own
    // linked plate and (per job) deferring to the default fallback.
    private int PlateCombo(string label, int plate, bool fallback, bool allowDefault = false)
    {
        using var combo = ImRaii.Combo(label, Plates.Label(config, plate));
        if (!combo)
            return plate;
        if (allowDefault && ImGui.Selectable(Plates.Label(config, Configuration.UseDefault), plate == Configuration.UseDefault))
            plate = Configuration.UseDefault;
        if (fallback)
        {
            if (ImGui.Selectable(Plates.Label(config, Configuration.LeaveAlone), plate == Configuration.LeaveAlone))
                plate = Configuration.LeaveAlone;
            if (ImGui.Selectable(Plates.Label(config, Configuration.LinkedPlate), plate == Configuration.LinkedPlate))
                plate = Configuration.LinkedPlate;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Re-equips your gear set with the plate linked to it in the gear set list.\nHandy as \"go back to normal\" when you leave a rule's area.");
            ImGui.Separator();
        }
        for (var i = 1; i <= Plates.Count; i++)
        {
            if (ImGui.Selectable(Plates.Label(config, i), plate == i))
                plate = i;
            if (ImGui.IsItemHovered())
                PlateTooltip(i);
        }
        return plate;
    }

    private void PlateTooltip(int plate)
    {
        var items = Plates.Items(plate);
        if (!Plates.Loaded)
            ImGui.SetTooltip("Open your glamour plates in game once to see what's on each one.");
        else if (items.Count > 0)
            ImGui.SetTooltip(string.Join("\n", items));
        else
            ImGui.SetTooltip("(empty plate)");
    }

    public static bool GlamourerLoaded =>
        Services.PluginInterface.InstalledPlugins.Any(p => p.InternalName == "Glamourer" && p.IsLoaded);

    // Glamourer draws its own designs over the game's gear, so a plate can
    // go on without anything seeming to change.
    private void DrawGlamourerNotice()
    {
        if (config.GlamourerNoticeDismissed || !GlamourerLoaded)
            return;
        using (ImRaii.PushColor(ImGuiCol.ChildBg, new Vector4(0.45f, 0.35f, 0.1f, 0.35f)))
        using (var box = ImRaii.Child("##glamourer", new Vector2(-1, ImGui.GetTextLineHeightWithSpacing() * 3.4f), true))
        {
            if (box)
            {
                ImGui.TextColored(Warn, "Glamourer is running.");
                ImGui.SameLine();
                ImGui.TextWrapped("If Glamourer has a design or automation active for this character, it covers your plates and it'll look like nothing happened. Turn that off, or use Glamourer's \"Use game state\" options, for plates to show.");
                if (ImGui.SmallButton("Got it, don't show again"))
                {
                    config.GlamourerNoticeDismissed = true;
                    config.Save();
                }
            }
        }
    }

    private Keybind? capturing;

    private void KeybindRow(string label, Keybind bind)
    {
        using var id = ImRaii.PushId(label);
        if (capturing == bind)
        {
            ImGui.TextColored(Warn, "Press a key... (Esc to cancel)");
            var keys = Services.KeyState;
            if (keys[VirtualKey.ESCAPE])
            {
                capturing = null;
            }
            else
            {
                foreach (var key in keys.GetValidVirtualKeys())
                {
                    if (key is VirtualKey.CONTROL or VirtualKey.MENU or VirtualKey.SHIFT or VirtualKey.LCONTROL or VirtualKey.RCONTROL
                        or VirtualKey.LMENU or VirtualKey.RMENU or VirtualKey.LSHIFT or VirtualKey.RSHIFT or VirtualKey.LBUTTON
                        or VirtualKey.RBUTTON or VirtualKey.MBUTTON)
                        continue;
                    if (!keys[key])
                        continue;
                    bind.Key = (int)key;
                    bind.Ctrl = keys[VirtualKey.CONTROL];
                    bind.Alt = keys[VirtualKey.MENU];
                    bind.Shift = keys[VirtualKey.SHIFT];
                    keys[key] = false;
                    capturing = null;
                    config.Save();
                    break;
                }
            }
        }
        else if (ImGui.Button($"{bind}##bind", new Vector2(140 * ImGuiHelpers.GlobalScale, 0)))
        {
            capturing = bind;
        }
        ImGui.SameLine();
        ImGui.TextUnformatted(label);
        if (bind.Key != 0)
        {
            ImGui.SameLine();
            if (ImGui.SmallButton("Clear"))
            {
                bind.Key = 0;
                bind.Ctrl = bind.Alt = bind.Shift = false;
                config.Save();
            }
        }
    }

    private static void Hint(string text)
    {
        ImGui.SameLine();
        ImGui.TextDisabled("(?)");
        if (ImGui.IsItemHovered())
        {
            using var tooltip = ImRaii.Tooltip();
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 28);
            ImGui.TextUnformatted(text);
            ImGui.PopTextWrapPos();
        }
    }
}
