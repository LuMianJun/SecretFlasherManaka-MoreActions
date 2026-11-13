namespace UnityEngine
{
    public sealed class AnimationClip : Object
    {
        public float length=2;public Action<GameObject,float> Evaluate;
        public void SampleAnimation(GameObject target,float seconds)=>Evaluate?.Invoke(target,seconds);
    }
}
namespace SFM.Actions
{
    public sealed class ActionDefinition
    {
        public bool WriteHipTranslation=true,WriteFaceMorphs=true,KeepGameExpressions=true,Loop;
        public string[] BonePaths=Array.Empty<string>();
    }
    public static class ActionPolicy
    {
        public static bool WritesBone(ActionDefinition action,string path)=>action.BonePaths.Length==0||action.BonePaths.Contains(path);
    }
    internal sealed class LoadedAction
    {
        public ActionDefinition Definition;public UnityEngine.AnimationClip Clip;
        public float Duration=>Clip.length;
    }
    // The native graph has its own interop/clock validation. This boundary only
    // supplies pose samples so real ActionPose transfer/restore code is tested.
    internal sealed class AnimatorClipSampler : IDisposable
    {
        internal static bool FailConstruction,FailDisposal;
        internal static UnityEngine.GameObject LastProxy;
        readonly UnityEngine.GameObject proxy;readonly UnityEngine.AnimationClip clip;
        internal AnimatorClipSampler(UnityEngine.GameObject target,UnityEngine.Animator animator,UnityEngine.AnimationClip clip){proxy=target;this.clip=clip;LastProxy=target;if(FailConstruction)throw new InvalidOperationException("fixture graph setup/cleanup failure");}
        internal void Sample(float seconds)=>clip.SampleAnimation(proxy,seconds);
        public void Dispose(){if(FailDisposal)throw new InvalidOperationException("fixture graph disposal failure");}
    }
    public sealed class Logger {public void LogWarning(string value){} }
    public static class Plugin {public static Logger LogSource=new();}
}
