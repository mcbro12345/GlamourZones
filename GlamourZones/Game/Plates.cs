using System;
using System.Collections.Generic;
using System.Text;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;

namespace GlamourZones.Game;

// Applies plates through the gear set module, and reads what's on each
// plate once the game has loaded them.
public static class Plates
{
    public const int DefaultCount = 20;

    public static unsafe int Count
    {
        get
        {
            var mirage = MirageManager.Instance();
            return mirage == null || mirage->GlamourPlates.Length == 0 ? DefaultCount : mirage->GlamourPlates.Length;
        }
    }

    // The game only fills these in after the plate list has been opened once
    // this session (at a Glamour Dresser or from the gear set list).
    public static unsafe bool Loaded
    {
        get
        {
            var mirage = MirageManager.Instance();
            return mirage != null && mirage->GlamourPlatesLoaded;
        }
    }

    public static unsafe List<string> Items(int plate)
    {
        var items = new List<string>();
        var mirage = MirageManager.Instance();
        if (mirage == null || !mirage->GlamourPlatesLoaded || plate < 1 || plate > mirage->GlamourPlates.Length)
            return items;
        foreach (var id in mirage->GlamourPlates[plate - 1].ItemIds)
        {
            var name = GameInfo.ItemName(id);
            if (name.Length > 0)
                items.Add(name);
        }
        return items;
    }

    public static string Label(Configuration config, int plate) => plate switch
    {
        Configuration.LeaveAlone => "Leave as is",
        Configuration.LinkedPlate => "Gear set's linked plate",
        Configuration.UseDefault => "Default fallback",
        _ => config.PlateNames.TryGetValue(plate, out var name) && !string.IsNullOrWhiteSpace(name) ? $"Plate {plate}: {name}" : $"Plate {plate}",
    };

    // The gear set that would be used: the one being worn, or else (if
    // allowed) the first saved set for the current job. -1 if none.
    public static unsafe int GearsetFor(uint job, bool allowJobGearset)
    {
        var gearsets = RaptureGearsetModule.Instance();
        if (gearsets == null)
            return -1;
        var current = gearsets->CurrentGearsetIndex;
        if (current >= 0 && gearsets->IsValidGearset(current))
            return current;
        if (!allowJobGearset)
            return -1;
        for (var i = 0; i < 100; i++)
        {
            if (gearsets->IsValidGearset(i) && gearsets->GetGearset(i)->ClassJob == job)
                return i;
        }
        return -1;
    }

    public static unsafe bool IsWearingGearset
    {
        get
        {
            var gearsets = RaptureGearsetModule.Instance();
            return gearsets != null && gearsets->CurrentGearsetIndex >= 0 && gearsets->IsValidGearset(gearsets->CurrentGearsetIndex);
        }
    }

    public static unsafe string GearsetName(int index)
    {
        var gearsets = RaptureGearsetModule.Instance();
        if (gearsets == null || index < 0 || !gearsets->IsValidGearset(index))
            return "(none)";
        return $"{index + 1}: {gearsets->GetGearset(index)->NameString}";
    }

    // Equips the gear set with the plate on top, the same thing the gear set
    // list does when a set is linked to a plate. Returns why it couldn't, or
    // null when it went through.
    public static unsafe string? Apply(int gearset, int plate)
    {
        var gearsets = RaptureGearsetModule.Instance();
        if (gearsets == null)
            return "gear sets aren't loaded";
        if (gearset < 0 || !gearsets->IsValidGearset(gearset))
            return "you're not wearing a saved gear set and none is saved for this job";
        if (plate == Configuration.LinkedPlate)
        {
            plate = gearsets->GetGearset(gearset)->GlamourSetLink;
            if (plate == 0)
                return "this gear set isn't linked to a plate";
        }
        if (plate < 1 || plate > Count)
            return $"there's no plate {plate}";
        var result = gearsets->EquipGearset(gearset, (byte)plate);
        return result < 0 ? $"the game refused (code {result})" : null;
    }

    // What's worn right now, item and glamour and dyes per slot, to tell
    // whether a plate is still on.
    public static unsafe string Fingerprint()
    {
        var inventory = InventoryManager.Instance();
        var container = inventory == null ? null : inventory->GetInventoryContainer(InventoryType.EquippedItems);
        if (container == null)
            return string.Empty;
        var text = new StringBuilder();
        for (var i = 0; i < container->Size; i++)
        {
            var item = container->GetInventorySlot(i);
            if (item == null)
                continue;
            text.Append(item->ItemId).Append(':').Append(item->GlamourId).Append(':')
                .Append(item->GetStain(0)).Append(':').Append(item->GetStain(1)).Append(';');
        }
        return text.ToString();
    }

    // Runs one text command as if typed into the chat box. Anything not
    // starting with / is refused so nothing is ever said in chat.
    public static unsafe bool RunCommand(string command)
    {
        command = command.Trim();
        if (!command.StartsWith('/') || command.Length > 500 || command.AsSpan().ContainsAny('\n', '\r'))
            return false;
        var ui = UIModule.Instance();
        if (ui == null)
            return false;
        var message = Utf8String.FromString(command);
        try
        {
            ui->ProcessChatBoxEntry(message, IntPtr.Zero, false);
        }
        finally
        {
            message->Dtor(true);
        }
        return true;
    }
}
