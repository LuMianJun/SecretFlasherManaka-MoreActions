using System.Text;
using SFM.Actions;
using UnityEngine;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
int checks=0;
void Check(bool value,string label){checks++;if(!value)throw new Exception(label);}
const string hips="Armature/Hips",chest=hips+"/Spine/Chest";
var positions=new Dictionary<string,Vector3>
{
    [hips]=new(0,1,0),[hips+"/Spine"]=new(0,1.1f,0),[chest]=new(0,1.35f,0),
    [chest+"/Neck"]=new(0,1.5f,0),[chest+"/Neck/Head"]=new(0,1.65f,0)
};
foreach(string suffix in new[]{"_L","_R"})
{
    float side=suffix=="_L"?-1:1;
    string shoulder=chest+"/Shoulder"+suffix,arm=shoulder+"/UpperArm"+suffix,elbow=arm+"/LowerArm"+suffix,hand=elbow+"/Hand"+suffix;
    positions[shoulder]=new(side*.08f,1.4f,0);positions[arm]=new(side*.17f,1.4f,0);
    positions[elbow]=new(side*.4f,1.24f,0);positions[hand]=new(side*.6f,1.1f,0);
    foreach(var (finger,n) in new[]{"Thumb","Index","Middle","Ring","Little"}.Select((finger,n)=>(finger,n)))
    {
        string path=hand;
        foreach(var (segment,index) in new[]{"Proximal","Intermediate","Distal"}.Select((segment,index)=>(segment,index)))
        {path+="/"+finger+segment+suffix;positions[path]=new(side*(.62f+index*.025f),1.1f,n*.015f);}
    }
    string leg=hips+"/UpperLeg"+suffix,knee=leg+"/LowerLeg"+suffix,foot=knee+"/Foot"+suffix;
    positions[leg]=new(side*.1f,.95f,0);positions[knee]=new(side*.1f,.55f,.02f);
    positions[foot]=new(side*.1f,.15f,0);positions[foot+"/Toe"+suffix]=new(side*.1f,.08f,.15f);
}
(GameObject avatar,Dictionary<string,Transform> bones,SkinnedMeshRenderer renderer) Avatar(
    IReadOnlyDictionary<string,Vector3> geometry=null,IReadOnlyDictionary<string,Matrix4x4> rest=null)
{
    geometry??=positions;
    rest??=geometry.ToDictionary(p=>p.Key,p=>Matrix4x4.TRS(p.Value,Quaternion.AngleAxis(p.Key.Length%7*10,Vector3.up),Vector3.one));
    var root=new GameObject("NativeAvatar");var armature=new GameObject("Armature").transform;
    armature.SetParent(root.transform,false);armature.localRotation=Quaternion.AngleAxis(-90,Vector3.right);
    var bones=new Dictionary<string,Transform>();
    foreach(string path in geometry.Keys.OrderBy(p=>p.Count(c=>c=='/')).ThenBy(p=>p,StringComparer.Ordinal))
    {
        int slash=path.LastIndexOf('/');var bone=new GameObject(path[(slash+1)..]).transform;
        bone.SetParent(root.transform.Find(path[..slash]),false);bone.position=geometry[path];bone.rotation=rest[path].rotation;bones.Add(path,bone);
    }
    var body=new GameObject("Body.001");body.transform.SetParent(root.transform,false);
    var shapes=Enumerable.Range(0,FixedModelFace.ShapeCount).Select(i=>"shape"+i).ToArray();shapes[0]="vrc.v_aa";shapes[19]="eye_close";
    foreach(var entry in FixedModelFace.MmdMorphs)shapes[entry.Index]=entry.Name;
    var renderer=body.AddComponent<SkinnedMeshRenderer>();renderer.bones=bones.Values.ToArray();
    renderer.sharedMesh=new(){ShapeNames=shapes,bindposes=bones.Keys.Select(p=>rest[p].inverse).ToArray()};
    return(root,bones,renderer);
}

try
{
    ReaderCorrectnessRegression.Run(Check);
    AdapterGeometryRegression.Run(Check);
    PlaybackLifecycleRegression.Run(Check);
    ControlFrameRegression.Run(Check,positions);
    NativeBundleRegression.Run(Check);
    AdditionalTracksRegression.Run(Check);
    UpstreamCompatibilityRegression.Run(Check,positions,Avatar);
    if(args.Length>0)
    {
        if(args.Length!=2||args[0]!="--motions")throw new ArgumentException("Usage: CompatibilityValidation [--motions <six-motion directory>]");
        RealMotionCompatibilityRegression.Run(Check,args[1]);
    }
    Console.WriteLine("UPSTREAM_GAME_COMPATIBILITY PASS checks="+checks+" nativeAxes=true bodyDimensions=true rootOwnership=true nativeRestore=true extensionsPreserved=true");
}
catch(Exception error)
{Console.Error.WriteLine(error);Environment.ExitCode=1;}
