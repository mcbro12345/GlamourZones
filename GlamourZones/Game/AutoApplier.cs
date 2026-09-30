using System;
using System.Collections.Generic;
using System.Linq;

namespace GlamourZones.Game;

// Watches job, place, time and weather, picks the first matching rule and
// puts its plate on once the game allows it.
public sealed class AutoApplier
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan CommandGap = TimeSpan.FromMilliseconds(600);
    // How long after equipping to read back what's worn, once the server
    // has sent the new gear.
    private static readonly TimeSpan SettleTime = TimeSpan.FromSeconds(2.5);

    private readonly Configuration config;
    private readonly Queue<string> commands = new();
    private DateTime nextCheck;
    private DateTime nextCommand;
    private uint lastJob;
    private uint lastTerritory;
    private string? lastRuleKey;
    private DateTime pendingAt = DateTime.MaxValue;
    private bool pendingForced;
    private string pendingReason = string.Empty;
    private uint pausedInTerritory;
    private bool toldAboutBlocker;
    private (ulong ContentId, int Plate, DateTime At)? recordAfterApply;

    public AutoApplier(Configuration config) => this.config = config;

    public Snapshot? Now { get; private set; }
    public Rule? Current { get; private set; }
    public int CurrentPlate => Current?.Plate ?? (Now == null ? config.FallbackPlate : config.FallbackFor(Now.Job));
    public bool Pending => pendingAt != DateTime.MaxValue;
    public string PendingReason => pendingReason;
    public string Waiting { get; private set; } = string.Empty;
    public string LastApplied { get; private set; } = "Nothing yet";
    public bool Paused => pausedInTerritory != 0;

    // Stops applying until the next zone change.
    public void PauseHere()
    {
        pausedInTerritory = Now?.Territory ?? Services.ClientState.TerritoryType;
        CancelPending();
    }

    public void Resume() => pausedInTerritory = 0;

    // Applies even if the plate already seems to be on.
    public void ApplyNow(string reason = "manual")
    {
        pausedInTerritory = 0;
        Schedule(reason, immediate: true);
        pendingForced = true;
    }

    public void CancelPending()
    {
        pendingAt = DateTime.MaxValue;
        pendingForced = false;
        Waiting = string.Empty;
        toldAboutBlocker = false;
    }

    public void Update()
    {
        var utcNow = DateTime.UtcNow;
        if (commands.Count > 0 && utcNow >= nextCommand)
        {
            nextCommand = utcNow + CommandGap;
            Plates.RunCommand(commands.Dequeue());
        }

        if (utcNow < nextCheck)
            return;
        nextCheck = utcNow + CheckInterval;

        var now = Snapshot.Take();
        Now = now;
        if (now == null)
        {
            lastJob = lastTerritory = 0;
            lastRuleKey = null;
            recordAfterApply = null;
            CancelPending();
            return;
        }

        RememberArea(now);
        RecordWhatsWorn(now, utcNow);
        Current = config.Rules.FirstOrDefault(r => config.IsActive(r) && r.Matches(now));
        var ruleKey = Current?.Id.ToString() ?? $"fallback{config.FallbackFor(now.Job)}";

        if (now.Territory != lastTerritory && lastTerritory != 0)
            pausedInTerritory = 0;

        if (config.Enabled && !Paused)
        {
            if (lastTerritory == 0)
                Schedule("login");
            else if (now.Territory != lastTerritory && config.ReapplyOnZoneChange)
                Schedule("zone change");
            else if (now.Job != lastJob && config.ReapplyOnJobChange)
                Schedule("job change");
            else if (ruleKey != lastRuleKey && (config.ReactInsideZone || now.Territory != lastTerritory || now.Job != lastJob))
                Schedule(now.Territory != lastTerritory ? "zone change" : now.Job != lastJob ? "job change" : "conditions changed");
        }
        lastJob = now.Job;
        lastTerritory = now.Territory;
        lastRuleKey = ruleKey;

        if (!Pending || utcNow < pendingAt)
            return;
        if (!config.Enabled || Paused)
        {
            CancelPending();
            return;
        }

        var plate = CurrentPlate;
        if (plate == Configuration.LeaveAlone)
        {
            CancelPending();
            return;
        }

        var gearset = Plates.GearsetFor(now.Job, config.UseJobGearset);
        var blocker = Blocker(now, gearset);
        if (blocker != null)
        {
            if (blocker == "not in a resting area" && !config.WaitForRestingArea)
            {
                CancelPending();
                return;
            }
            Waiting = blocker;
            // Say once why nothing is happening when it won't pass on its own.
            if (!toldAboutBlocker && blocker.StartsWith("no gear set"))
            {
                toldAboutBlocker = true;
                Services.Chat.PrintError($"Can't apply {Plates.Label(config, plate)}: {blocker}. Save a gear set for this job, or put one on.", "Glamour Zones");
            }
            return;
        }

        if (!pendingForced && config.SkipIfAlreadyOn && AlreadyOn(now, plate, gearset))
        {
            Services.Log.Debug($"Plate {plate} is already on, skipping ({pendingReason})");
            CancelPending();
            return;
        }

        Apply(now, gearset, plate, Current);
        CancelPending();
    }

    private void Schedule(string reason, bool immediate = false)
    {
        pendingReason = reason;
        pendingAt = immediate ? DateTime.UtcNow : DateTime.UtcNow + TimeSpan.FromSeconds(Math.Max(0, config.DelaySeconds));
        pendingForced = false;
        Waiting = string.Empty;
        toldAboutBlocker = false;
    }

    private string? Blocker(Snapshot now, int gearset)
    {
        if (now.Busy)
            return "busy (loading, cutscene, casting or in a duty)";
        if (config.SkipInCombat && now.InCombat)
            return "in combat";
        if (!now.InRestingArea)
            return "not in a resting area";
        if (gearset < 0)
            return config.UseJobGearset ? "no gear set is worn or saved for this job" : "no gear set is being worn";
        if (!now.GameAllowsPlates)
            return "the game isn't allowing plates right now";
        return null;
    }

    private bool AlreadyOn(Snapshot now, int plate, int gearset) =>
        config.LastApplied.TryGetValue(now.ContentId, out var last)
        && last.Plate == plate && last.Gearset == gearset
        && last.Fingerprint.Length > 0 && last.Fingerprint == Plates.Fingerprint();

    private void Apply(Snapshot now, int gearset, int plate, Rule? rule)
    {
        var why = rule == null ? "no rule matched, using the fallback" : $"rule \"{rule.Name}\"";
        var error = Plates.Apply(gearset, plate);
        if (error != null)
        {
            LastApplied = $"Couldn't apply {Plates.Label(config, plate)}: {error} ({DateTime.Now:t})";
            Services.Chat.PrintError($"Couldn't apply {Plates.Label(config, plate)}: {error}.", "Glamour Zones");
            Services.Log.Warning($"Plate {plate} failed: {error}");
            return;
        }

        config.LastApplied[now.ContentId] = new AppliedState { Plate = plate, Gearset = gearset };
        recordAfterApply = (now.ContentId, plate, DateTime.UtcNow + SettleTime);

        commands.Clear();
        if (rule != null)
        {
            foreach (var line in rule.ExtraCommands.Split('\n'))
            {
                var command = line.Trim();
                if (command.StartsWith('/'))
                    commands.Enqueue(command);
            }
        }
        // Give the gear change a moment before running extra commands.
        nextCommand = DateTime.UtcNow + CommandGap;

        LastApplied = $"{Plates.Label(config, plate)} on gear set {Plates.GearsetName(gearset)} ({why}, {pendingReason}) at {DateTime.Now:t}";
        if (config.ChatNotify)
            Services.Chat.Print($"Applied {Plates.Label(config, plate)} ({why}).", "Glamour Zones");
        Services.Log.Info($"Applied plate {plate} on gear set {gearset}: {why}, {pendingReason}");
    }

    // Once the new gear has arrived, remember exactly what's worn so the
    // same plate isn't put on again while nothing has changed.
    private void RecordWhatsWorn(Snapshot now, DateTime utcNow)
    {
        if (recordAfterApply is not { } record || utcNow < record.At || now.Busy)
            return;
        recordAfterApply = null;
        if (record.ContentId != now.ContentId || !config.LastApplied.TryGetValue(now.ContentId, out var state) || state.Plate != record.Plate)
            return;
        state.Fingerprint = Plates.Fingerprint();
        config.Save();
    }

    private void RememberArea(Snapshot now)
    {
        if (now.Territory == 0)
            return;
        if (!config.KnownAreas.TryGetValue(now.Territory, out var areas))
            config.KnownAreas[now.Territory] = areas = [];
        var changed = false;
        if (now.Area != 0)
            changed |= areas.Add(now.Area);
        if (now.SubArea != 0)
            changed |= areas.Add(now.SubArea);
        if (changed)
            config.Save();
    }
}
