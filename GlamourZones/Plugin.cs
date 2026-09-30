using System;
using System.Linq;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Game.Command;
using Dalamud.Game.Gui.Dtr;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;
using GlamourZones.Game;
using GlamourZones.UI;

namespace GlamourZones;

// Applies glamour plates automatically by job, place, time and weather.
public sealed class Plugin : IDalamudPlugin
{
    private const string Command = "/glamourzones";
    private const string ShortCommand = "/gzones";

    private readonly Configuration config;
    private readonly AutoApplier applier;
    private readonly WindowSystem windows = new("GlamourZones");
    private readonly MainWindow mainWindow;
    private IDtrBarEntry? dtrEntry;
    private string dtrText = string.Empty;
    private bool toggleHeld;
    private bool applyHeld;

    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        pluginInterface.Create<Services>();
        config = Services.PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        applier = new AutoApplier(config);
        RestingAreas.Load([.. GameInfo.Expansions.SelectMany(e => e.Regions).SelectMany(r => r.ZoneGroups).SelectMany(g => g.Places).SelectMany(p => p.TerritoryIds)]);
        mainWindow = new MainWindow(config, applier);
        windows.AddWindow(mainWindow);

        var help = new CommandInfo(OnCommand)
        {
            HelpMessage = "Open Glamour Zones. Also: on, off, toggle, apply, pause, resume, status, group <name> [on|off].",
        };
        Services.Commands.AddHandler(Command, help);
        Services.Commands.AddHandler(ShortCommand, new CommandInfo(OnCommand) { HelpMessage = "Same as /glamourzones.", ShowInHelp = false });

        Services.PluginInterface.UiBuilder.Draw += windows.Draw;
        Services.PluginInterface.UiBuilder.OpenMainUi += ToggleMain;
        Services.PluginInterface.UiBuilder.OpenConfigUi += OpenSettings;
        Services.Framework.Update += OnUpdate;
    }

    public void Dispose()
    {
        Services.Framework.Update -= OnUpdate;
        Services.PluginInterface.UiBuilder.Draw -= windows.Draw;
        Services.PluginInterface.UiBuilder.OpenMainUi -= ToggleMain;
        Services.PluginInterface.UiBuilder.OpenConfigUi -= OpenSettings;
        Services.Commands.RemoveHandler(Command);
        Services.Commands.RemoveHandler(ShortCommand);
        dtrEntry?.Remove();
        windows.RemoveAllWindows();
        config.Save();
    }

    private void ToggleMain() => mainWindow.Toggle();

    private void OpenSettings() => mainWindow.OpenTab("Settings");

    private void OnCommand(string command, string args)
    {
        args = args.Trim();
        if (args.StartsWith("group ", StringComparison.OrdinalIgnoreCase))
        {
            SetGroup(args[6..].Trim());
            return;
        }
        switch (args.ToLowerInvariant())
        {
            case "":
                ToggleMain();
                break;
            case "on":
                SetEnabled(true);
                break;
            case "off":
                SetEnabled(false);
                break;
            case "toggle":
                SetEnabled(!config.Enabled);
                break;
            case "apply":
                applier.ApplyNow();
                break;
            case "pause":
                applier.PauseHere();
                Services.Chat.Print("Paused until you change zone.", "Glamour Zones");
                break;
            case "resume":
                applier.Resume();
                applier.ApplyNow("resumed");
                break;
            case "status":
                mainWindow.OpenTab("Status");
                break;
            case "settings" or "config":
                OpenSettings();
                break;
            default:
                Services.Chat.PrintError($"Unknown option. Try {ShortCommand} on, off, toggle, apply, pause, resume, status or settings.", "Glamour Zones");
                break;
        }
    }

    // "/gzones group Winter on|off|toggle"; the state word is optional.
    private void SetGroup(string text)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var state = words.Length > 1 ? words[^1].ToLowerInvariant() : "toggle";
        var name = state is "on" or "off" or "toggle" && words.Length > 1 ? string.Join(' ', words[..^1]) : text;
        if (state is not ("on" or "off" or "toggle"))
            state = "toggle";
        var group = config.Rules.Select(r => r.Group).FirstOrDefault(g => g.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (group == null || group.Length == 0)
        {
            Services.Chat.PrintError($"No group called \"{name}\".", "Glamour Zones");
            return;
        }
        var on = state == "on" || (state == "toggle" && config.DisabledGroups.Contains(group));
        if (on)
            config.DisabledGroups.Remove(group);
        else
            config.DisabledGroups.Add(group);
        config.Save();
        Services.Chat.Print($"Group \"{group}\" {(on ? "on" : "off")}.", "Glamour Zones");
    }

    private void SetEnabled(bool enabled)
    {
        config.Enabled = enabled;
        config.Save();
        Services.Chat.Print(enabled ? "Automatic plates on." : "Automatic plates off.", "Glamour Zones");
        if (enabled)
            applier.ApplyNow("enabled");
    }

    private void OnUpdate(IFramework framework)
    {
        HandleKeybinds();
        applier.Update();
        UpdateDtr();
    }

    // Keybinds fire once per press and are ignored while typing anywhere.
    private unsafe void HandleKeybinds()
    {
        var typing = Dalamud.Bindings.ImGui.ImGui.GetIO().WantTextInput;
        var atk = RaptureAtkModule.Instance();
        if (atk != null && atk->IsTextInputActive())
            typing = true;
        if (Pressed(config.ToggleKey, ref toggleHeld, typing))
            SetEnabled(!config.Enabled);
        if (Pressed(config.ApplyKey, ref applyHeld, typing))
            applier.ApplyNow();
    }

    private static bool Pressed(Keybind bind, ref bool held, bool typing)
    {
        if (bind.Key == 0)
            return false;
        var keys = Services.KeyState;
        var key = (VirtualKey)bind.Key;
        var down = keys[key] && keys[VirtualKey.CONTROL] == bind.Ctrl && keys[VirtualKey.MENU] == bind.Alt && keys[VirtualKey.SHIFT] == bind.Shift;
        if (!down)
        {
            held = false;
            return false;
        }
        if (held || typing)
            return false;
        held = true;
        keys[key] = false; // keep the game from also acting on it
        return true;
    }

    private void UpdateDtr()
    {
        if (!config.ShowDtr)
        {
            if (dtrEntry != null)
                dtrEntry.Shown = false;
            return;
        }
        dtrEntry ??= CreateDtrEntry();
        dtrEntry.Shown = true;

        var plate = applier.CurrentPlate;
        var text = !config.Enabled ? "Plates off"
            : applier.Paused ? "Plates paused"
            : plate > 0 ? $"Plate {plate}{(applier.Pending ? "…" : string.Empty)}"
            : "No plate";
        text = $"{SeIconChar.BoxedLetterG.ToIconString()} {text}";
        if (text == dtrText)
            return;
        dtrText = text;
        dtrEntry.Text = text;
        var rule = applier.Current?.Name ?? (plate > 0 ? "Fallback plate" : "No rule matches");
        dtrEntry.Tooltip = new SeStringBuilder().AddText($"Glamour Zones\n{rule}\nClick to turn on/off, right-click the plugin for settings").Build();
    }

    private IDtrBarEntry CreateDtrEntry()
    {
        var entry = Services.DtrBar.Get("Glamour Zones");
        entry.OnClick = _ => SetEnabled(!config.Enabled);
        return entry;
    }
}
