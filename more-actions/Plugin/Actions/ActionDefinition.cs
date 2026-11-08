using System;

namespace SFM.Actions;

public enum ActionBodyMask { FullBody, UpperBody, LowerBody }

// A pack contains a Generic clip already retargeted to the game's canonical rig.
// Registry entries never become original SkillType, ActionType or save records.
public sealed class ActionDefinition
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Description { get; set; } = "";
    public string Bundle { get; set; } = "";
    public string ClipAsset { get; set; } = "";
    public string AudioAsset { get; set; } = "";
    public string AudioFile { get; set; } = "";
    public double AudioDelaySeconds { get; set; }
    public float Volume { get; set; } = 1;
    public bool RequiresHands { get; set; } = true;
    public bool RequiresStanding { get; set; } = true;
    public bool FollowFaceCamera { get; set; } = true;
    public bool WriteHipTranslation { get; set; } = true;
    public bool Loop { get; set; }
    public ActionBodyMask BodyMask { get; set; } = ActionBodyMask.FullBody;
    public string[] BonePaths { get; set; } = Array.Empty<string>();
    public bool WriteFaceMorphs { get; set; } = true;
    public bool KeepGameExpressions { get; set; } = true;
    public bool ShowInRing { get; set; } = true;
    public bool UseClipEvents { get; set; } = true;
    public ActionEventDefinition[] Events { get; set; } = Array.Empty<ActionEventDefinition>();
    public ActionEventDefinition[] OnStart { get; set; } = Array.Empty<ActionEventDefinition>();
    public ActionEventDefinition[] OnFinish { get; set; } = Array.Empty<ActionEventDefinition>();
    public ActionEventDefinition[] OnCancel { get; set; } = Array.Empty<ActionEventDefinition>();
    public string NativeKind { get; set; } = "None";
    public ActionStatsDefinition Stats { get; set; } = new();
    public bool PoseMission { get; set; }
    public string ProviderId { get; internal set; } = "";
    public string BundlePath { get; internal set; } = "";
    public string AudioPath { get; internal set; } = "";
    [System.Text.Json.Serialization.JsonIgnore]
    public IActionSource RuntimeSource { get; internal set; }
}
