using System;
using static SFM.Actions.ExternalActionChannels;

namespace SFM.Actions;

internal sealed class ExternalActionProfiles
{
    readonly Entry[] entries;
    internal bool FollowFaceCamera { get; }
    internal bool PoseMissionAvailable { get; }
    internal string IkuResource { get; }

    private ExternalActionProfiles(Entry[] entries, bool followFaceCamera, string ikuResource, bool poseMission)
    {
        this.entries = entries;
        FollowFaceCamera = followFaceCamera;
        IkuResource = ikuResource;
        PoseMissionAvailable = poseMission;
    }

    internal static bool TryBind(string directory, ExternalActionProfile[] profiles,
        out ExternalActionProfiles bound, out string reason)
    {
        bound = null;
        reason = null;
        if (profiles == null || profiles.Length == 0)
        {
            reason = "no action profiles";
            return false;
        }

        var entries = new Entry[profiles.Length];
        ExternalActionChannels owned = None;
        string ikuResource = null;
        bool faceCamera = true;
        bool poseMission = false;
        for (int i = 0; i < profiles.Length; i++)
        {
            var profile = profiles[i];
            if (profile == null ||
                !ExternalActionApi.TryGetConfigPath(directory, profile.ResourceName, out string path))
            {
                reason = "invalid action resource name";
                return false;
            }
            if ((profile.Channels & ~All) != None || (profile.Channels & owned) != None)
            {
                reason = "invalid or overlapping stat channels";
                return false;
            }
            if (profile.HasIku)
            {
                if (ikuResource != null)
                {
                    reason = "multiple IKU profiles";
                    return false;
                }
                ikuResource = profile.ResourceName;
            }
            try
            {
                var definition = ExternalActionApi.ReadConfiguration(path);
                if (i == 0) faceCamera = definition.FollowFaceCamera;
                entries[i] = new Entry(profile.Channels, NativeActionStats.Resolve(definition),
                    definition.PoseMission, profile.IsActive);
                poseMission |= (profile.Channels & PoseMission) != None && definition.PoseMission;
                owned |= profile.Channels;
            }
            catch (Exception error)
            {
                reason = "invalid action configuration";
                Plugin.LogSource.LogWarning($"EXTERNAL_ACTION_CONFIG_FAILED file={path} {error.Message}");
                return false;
            }
        }
        bound = new ExternalActionProfiles(entries, faceCamera, ikuResource, poseMission);
        return true;
    }

    internal ActionStatValues Sample(out bool poseMission)
    {
        var values = ActionStatValues.Empty;
        poseMission = false;
        foreach (var entry in entries)
        {
            if (entry.IsActive?.Invoke() == false) continue;
            var stats = entry.Stats;
            var channels = entry.Channels;
            if ((channels & Ecstasy) != None) values = values with { EcstasyPerSecond = stats.EcstasyPerSecond };
            if ((channels & Dokidoki) != None) values = values with { DokidokiAdd = stats.DokidokiAdd };
            if ((channels & Moisture) != None) values = values with { MoisturePerSecond = stats.MoisturePerSecond };
            if ((channels & Stamina) != None) values = values with { StaminaPerSecond = stats.StaminaPerSecond };
            if ((channels & Exposure) != None)
                values = values with { Strange = stats.Strange, StrangenessScale = stats.StrangenessScale };
            if ((channels & Discomfort) != None) values = values with { Discomfort = stats.Discomfort };
            if ((channels & Pee) != None) values = values with { CanStartPee = stats.CanStartPee };
            if ((channels & PoseMission) != None) poseMission = entry.PoseMission;
        }
        return values;
    }

    private readonly record struct Entry(ExternalActionChannels Channels, ActionStatValues Stats,
        bool PoseMission, Func<bool> IsActive);
}
