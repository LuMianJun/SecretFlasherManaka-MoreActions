using System.Text.Json;
using SFM.Mmd;
using UnityEngine;

// Test-only interop boundary; production reads the real matching GameState.
namespace ExposureUnnoticed2.Scripts.InGame
{
    internal static class GameState { internal static CustomData PlayerCustomData; }
    internal sealed class CustomData
    { internal int CustomizeNeck=50,CustomizeShoulder=50,CustomizeLegInterval=50; }
}

internal static class AdapterGeometryRegression
{
    internal static void Run(Action<bool,string> check)
    {
        using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","native-model-bind.json")));
        using var actor=new NativeBundleRegression.NativeActor(doc.RootElement,1.2f);
        var baseline=MmdTargetSnapshot.Read(actor.Root);
        var custom=new ExposureUnnoticed2.Scripts.InGame.CustomData();
        ExposureUnnoticed2.Scripts.InGame.GameState.PlayerCustomData=custom;
        try
        {
            var defaults=MmdTargetSnapshot.Read(actor.Root);
            check(defaults.Matches(baseline.Shape,baseline.ShapePaths),"Native customization defaults preserve reference geometry and cache shape");
            foreach(string dimension in new[]{"neck","shoulder","legs"})
            {
                custom.CustomizeNeck=dimension=="neck"?100:50;
                custom.CustomizeShoulder=dimension=="shoulder"?100:50;
                custom.CustomizeLegInterval=dimension=="legs"?100:50;
                var changed=MmdTargetSnapshot.Read(actor.Root);
                check(!changed.Matches(defaults.Shape,defaults.ShapePaths),"Native "+dimension+" change invalidates shape cache");
                foreach(var path in changed.Positions.Keys)
                {
                    string owner=dimension=="neck"?"Armature/Hips/Spine/Chest/Neck":dimension=="shoulder"?
                        (path.Contains("Shoulder_L")?"Armature/Hips/Spine/Chest/Shoulder_L":"Armature/Hips/Spine/Chest/Shoulder_R"):
                        (path.Contains("UpperLeg_L")?"Armature/Hips/UpperLeg_L":"Armature/Hips/UpperLeg_R");
                    Vector3 delta=Vector3.zero;
                    if(path==owner||path.StartsWith(owner+"/",StringComparison.Ordinal))
                    {
                        var q=defaults.Rest[owner].rotation;
                        delta=dimension=="legs"?q*Vector3.right*(owner.EndsWith("_L")?-.02f:.02f):q*Vector3.up*(dimension=="neck"?.025f:.05f);
                    }
                    var actual=changed.Positions[path]-defaults.Positions[path];
                    check(Vector3.Distance(new(actual.X,actual.Y,actual.Z),delta)<.00001f,"Native metric offset and descendant inheritance: "+dimension+" "+path);
                }
                actor.Bones["Armature/Hips/UpperLeg_L"].localRotation=Quaternion.Euler(40,80,20);
                var repeat=MmdTargetSnapshot.Read(actor.Root);
                check(repeat.Matches(changed.Shape,changed.ShapePaths)&&repeat.Positions.All(p=>p.Value==changed.Positions[p.Key]),"Repeated capture ignores animated pose and does not accumulate native "+dimension+" offsets");
            }
        }
        finally {ExposureUnnoticed2.Scripts.InGame.GameState.PlayerCustomData=null;}
    }
}
