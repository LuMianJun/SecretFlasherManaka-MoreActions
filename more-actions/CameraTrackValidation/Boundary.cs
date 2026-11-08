namespace HarmonyLib { [AttributeUsage(AttributeTargets.Class)] public sealed class HarmonyPatch:Attribute {public HarmonyPatch(Type t,string n){}} }
namespace ExposureUnnoticed2.Object3D.Player.Scripts
{
 public sealed class PlayerController {public Pca Pca;}
 public sealed class Pca {public UnityEngine.Animator Animator;public Referencer PlayerAvatarObjectReferencer;public Locomotion PlayerLocomotion;}
 public sealed class Referencer {public UnityEngine.Transform CameraTarget;}
 public sealed class Locomotion {public Fp fpCameraController;}
 public sealed class Fp {public UnityEngine.Transform transform,Child;}
}
namespace SFM.Actions
{
 internal static class NativeAnimationReplacements
 {
  internal static bool Bound=true;internal static Dictionary<UnityEngine.AnimationClip,UnityEngine.AnimationClip> Sources=new();
  internal static bool IsBoundTo(UnityEngine.Animator a)=>Bound;
  internal static bool TryGetCameraSource(UnityEngine.AnimationClip clip,out UnityEngine.AnimationClip original)=>Sources.TryGetValue(clip,out original);
 }
 public static class Plugin {public static Logger LogSource=new();}
 public sealed class Logger {public void LogInfo(string s){}public void LogWarning(string s){}}
}
