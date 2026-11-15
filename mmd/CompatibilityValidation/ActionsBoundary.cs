// Only the action configuration/log boundary is mocked. Rig preparation,
// body ownership, morph ownership and VMD pose output link production source.
namespace SFM.Actions
{
    public enum ActionBodyMask { FullBody,UpperBody,LowerBody }
    public sealed class ActionDefinition
    {
        public string Id="compatibility",ProviderId="test";
        public ActionBodyMask BodyMask;
        public string[] BonePaths=[];
        public bool KeepGameExpressions=true,WriteHipTranslation=true,WriteFaceMorphs=true;
        public bool RequiresHands=true,RequiresStanding=true,Loop;
    }
    public sealed class Logger
    {
        public readonly List<string> Messages=new();
        public void LogInfo(string message)=>Messages.Add(message);
        public void LogWarning(string message)=>Messages.Add(message);
    }
    public static class Plugin { public static Logger LogSource=new(); }
}
public static class Plugin
{
    internal const string Version="compatibility-test";
    public static SFM.Actions.Logger LogSource=new();
}
namespace UnityEngine
{
    public sealed class AnimationClip:Object {}
    public enum HumanBodyBones
    {
        Hips,Spine,Chest,Neck,Head,
        LeftShoulder,LeftUpperArm,LeftLowerArm,LeftHand,
        RightShoulder,RightUpperArm,RightLowerArm,RightHand,
        LeftUpperLeg,LeftLowerLeg,LeftFoot,LeftToes,
        RightUpperLeg,RightLowerLeg,RightFoot,RightToes,
        LeftThumbProximal,LeftThumbIntermediate,LeftThumbDistal,
        LeftIndexProximal,LeftIndexIntermediate,LeftIndexDistal,
        LeftMiddleProximal,LeftMiddleIntermediate,LeftMiddleDistal,
        LeftRingProximal,LeftRingIntermediate,LeftRingDistal,
        LeftLittleProximal,LeftLittleIntermediate,LeftLittleDistal,
        RightThumbProximal,RightThumbIntermediate,RightThumbDistal,
        RightIndexProximal,RightIndexIntermediate,RightIndexDistal,
        RightMiddleProximal,RightMiddleIntermediate,RightMiddleDistal,
        RightRingProximal,RightRingIntermediate,RightRingDistal,
        RightLittleProximal,RightLittleIntermediate,RightLittleDistal,
        LeftEye,RightEye,Jaw,UpperChest,LastBone
    }
}
