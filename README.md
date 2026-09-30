# Glamour Zones

<img src="images/icon.png" width="96" align="right" alt="Glamour Zones icon">

A Dalamud plugin that puts on your glamour plates for you, based on your job and where you are.

"Red Mage in Ishgard wears plate 4", "any tank in Old Sharlayan at night wears plate 9", "everything outside housing uses plate 1". Make as many rules as you like. They're checked from the top down, and the first one that matches picks the plate.

## What a rule can check

- **Jobs**: click job icons, or a role name to pick the whole role. Base classes are available too. No jobs picked means any job.
- **Locations**: sorted by expansion, then region, then city or zone group, then single zone. Tick a whole region (Coerthas), a city with all its districts, inns and housing (Ishgard), one zone (The Pillars), or an area from under the minimap (The Jeweled Crozier). Areas are learned as you walk around. You can also turn a rule into "anywhere except these".
- **Housing**: inside your own house, your Free Company house, your private chambers, your apartment, or someone else's home.
- **Eorzea time**: an hour range that can wrap past midnight, with Day and Night presets.
- **Weather**: one or more kinds of weather.
- **Characters**: limit a rule to some of your characters.
- **Extra commands**: run commands after the plate goes on, such as `/visor`, `/facewear 3` or another plugin's command.

When nothing matches, a **fallback** is used. You can set a default plus a different one per job. A fallback can also be "leave as is", or "gear set's linked plate" to go back to normal when you leave a rule's area.

Rules can go in **groups** (say "Winter" or "Moonfire Faire") that you switch on and off together.

## How applying works

Plates go on the same way as from the gear set list: your current gear set is re-equipped with the plate on top, so it uses the gear set you're wearing (or, if you aren't wearing one, the first one saved for your job). If you're still wearing exactly what a plate put on, it isn't applied again, so gear you swapped by hand isn't undone for no reason. The game only lets you put a plate on in a resting area (cities, inns, housing and settlements), and not in combat or duties. Glamour Zones waits until it can, so if you ride into a field zone the plate goes on when you reach the local settlement. It applies when you:

- log in,
- change zone,
- change job,
- or move into a different area, or the time or weather changes (you can turn this off).

## Commands

| Command | What it does |
| --- | --- |
| `/gzones` | Open or close the window (`/glamourzones` works too) |
| `/gzones on`, `off`, `toggle` | Turn automatic plates on or off |
| `/gzones apply` | Apply the matching plate now |
| `/gzones pause` / `resume` | Stop applying until you change zone |
| `/gzones status` | Show what the plugin sees right now |
| `/gzones group <name> on` / `off` | Switch a group of rules on or off (handy in macros) |

The server info bar shows the current plate. Click it to turn the plugin on or off. You can also set keybinds for turning it on or off and for applying now.

If you use Glamourer, its designs and automation can cover your plates. The plugin warns you when Glamourer is running.

## Tips

- Give your plates names on the **Plates** tab. Once you've opened your plates in game, hovering over a plate shows what's on it.
- Use **Here** on the Rules tab to make a rule for your current job and city in one click.
- Drag rules to change their order. More specific rules should go above general ones.
- Rules can be copied to the clipboard and shared from the **Settings** tab.

## Install

Add `https://raw.githubusercontent.com/mcbro12345/DalamudPlugins/main/pluginmaster.json` to Dalamud's custom plugin repositories (`/xlsettings` → Experimental), then install Glamour Zones from `/xlplugins`.

## License

MIT. See [LICENSE](LICENSE) and [AI-GENERATED-NOTICE.md](AI-GENERATED-NOTICE.md).
