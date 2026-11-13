using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SFM.Actions;

int assertions=0;
void Check(bool condition,string message)
{
    assertions++;
    if(!condition)throw new InvalidOperationException(message);
}

string file=Path.Combine(Path.GetDirectoryName(typeof(MotionRig).Assembly.Location)!,"body-bones.json");
byte[] original=File.ReadAllBytes(file);
string json=File.ReadAllText(file);
var document=JsonNode.Parse(json)!.AsObject();
var bones=document["Bones"]!.AsArray();
var sourcePaths=bones.Select(b=>b!["Path"]!.GetValue<string>()).ToArray();
var requiredPaths=bones.Where(b=>b!["Required"]!.GetValue<bool>()).Select(b=>b!["Path"]!.GetValue<string>()).ToArray();
var sourceSet=new HashSet<string>(sourcePaths,StringComparer.Ordinal);

// All threads meet the same production lazy initializer on its first use.
var startup=Enumerable.Range(0,16).Select(_=>Task.Run(()=>
{
    MotionRig.Initialize();
    return MotionRig.BonePaths().Count()==80&&MotionRig.RequiredBonePaths().Count()==49;
})).ToArray();
Task.WaitAll(startup);
Check(startup.All(t=>t.Result),"Concurrent first use loads one valid shared character preset");
Check(sourcePaths.Length==80&&sourceSet.Count==80,"The actual shared resource contains 80 unique supported paths");
Check(requiredPaths.Length==49,"The actual shared resource retains the 49 required default paths");
Check(new HashSet<string>(MotionRig.BonePaths(),StringComparer.Ordinal).SetEquals(sourcePaths),"The public supported paths come from the shared resource");
Check(new HashSet<string>(MotionRig.RequiredBonePaths(),StringComparer.Ordinal).SetEquals(requiredPaths),"The public required paths come from the shared resource flags");
foreach(var bone in bones)
{
    string path=bone!["Path"]!.GetValue<string>();
    Check(MotionRig.IsSupportedPath(path),"The resource path is supported: "+path);
    Check(MotionRig.IsRequiredPath(path)==bone["Required"]!.GetValue<bool>(),"Required membership matches the resource: "+path);
    Check(MotionRig.MmdSource(path,sourceSet)==bone["Source"]!.GetValue<string>(),"A complete character uses the declared MMD source: "+path);
}
Check(!MotionRig.IsSupportedPath(null)&&!MotionRig.IsRequiredPath(null),"Null is never a supported or required body path");
Check(!MotionRig.IsSupportedPath("Armature/Hips/UnlistedBone"),"An unlisted path does not expand the fixed preset");
Check(MotionRig.MmdSource("Armature/Hips/UnlistedBone",sourceSet)==null,"Unknown body paths have no invented MMD mapping");

foreach(var bone in bones.Where(b=>b!["Fallback"]!=null))
{
    string path=bone!["Path"]!.GetValue<string>();
    var fallback=bone["Fallback"]!.AsObject();
    string missing=fallback["WhenMissing"]!.GetValue<string>();
    var available=new HashSet<string>(sourceSet,StringComparer.Ordinal);available.Remove(missing);
    Check(MotionRig.MmdSource(path,available)==fallback["Source"]!.GetValue<string>(),"A missing twist helper selects its declared fallback: "+path);
    Check(MotionRig.MmdSource(path,sourceSet)==bone["Source"]!.GetValue<string>(),"Restoring the helper restores the complete-rig mapping: "+path);
}
Check(bones.Count(b=>b!["Fallback"]!=null)==4,"Only the two upper-arm and two forearm mappings declare missing-twist fallbacks");
foreach(var side in new[]{(Suffix:"_L",Name:"左"),(Suffix:"_R",Name:"右")})
{
    string upper="Armature/Hips/Spine/Chest/Shoulder"+side.Suffix+"/UpperArm"+side.Suffix;
    string forearm=upper+"/LowerArm"+side.Suffix;
    string upperTwist=upper+"/UpperArm_twist"+side.Suffix;
    string forearmTwist=forearm+"/LowerArm_twist"+side.Suffix;
    Check(MotionRig.MmdSource(upper,sourceSet)==side.Name+"腕"&&MotionRig.MmdSource(forearm,sourceSet)==side.Name+"ひじ","A complete rig keeps the arm and elbow sources on the main joints: "+side.Suffix);
    Check(MotionRig.MmdSource(upperTwist,sourceSet)=="UpperArm_twist"+side.Suffix&&MotionRig.MmdSource(forearmTwist,sourceSet)=="LowerArm_twist"+side.Suffix,"Twist helpers retain their native synthetic bone source names: "+side.Suffix);
    var noTwists=new HashSet<string>(sourceSet,StringComparer.Ordinal);noTwists.Remove(upperTwist);noTwists.Remove(forearmTwist);
    Check(MotionRig.MmdSource(upper,noTwists)==side.Name+"腕捩"&&MotionRig.MmdSource(forearm,noTwists)==side.Name+"手捩","Without twist helpers their rotation is represented by the declared main-joint fallback: "+side.Suffix);
}

var parse=typeof(MotionRig).GetMethod("Parse",BindingFlags.NonPublic|BindingFlags.Static,new[]{typeof(string)})
    ??throw new InvalidOperationException("Production preset parser was not found");
Check(parse.Invoke(null,new object[]{json})!=null,"The production parser accepts the actual shared resource");

void Reject(string name,string input)
{
    try
    {
        parse.Invoke(null,new object[]{input});
    }
    catch(TargetInvocationException error) when(error.InnerException is JsonException or InvalidDataException or ArgumentException)
    {
        Check(true,"Rejected malformed preset: "+name);
        return;
    }
    throw new InvalidOperationException("Production parser accepted malformed preset: "+name);
}
void RejectChange(string name,Action<JsonObject> change)
{
    var changed=JsonNode.Parse(json)!.AsObject();change(changed);Reject(name,changed.ToJsonString());
}

Reject("damaged JSON","{");
Reject("null document","null");
RejectChange("unsupported schema",d=>d["Schema"]=2);
RejectChange("missing schema",d=>d.Remove("Schema"));
RejectChange("missing bone list",d=>d.Remove("Bones"));
RejectChange("null bone list",d=>d["Bones"]=null);
RejectChange("empty bone list",d=>d["Bones"]=new JsonArray());
RejectChange("duplicate path",d=>d["Bones"]!.AsArray().Add(d["Bones"]![0]!.DeepClone()));
RejectChange("null path",d=>d["Bones"]![0]!["Path"]=null);
RejectChange("outside body root",d=>d["Bones"]![0]!["Path"]="Accessory/Armature/Hips");
RejectChange("parent traversal",d=>d["Bones"]![0]!["Path"]="Armature/Hips/../Other");
RejectChange("empty source",d=>d["Bones"]![0]!["Source"]="");
RejectChange("null source",d=>d["Bones"]![0]!["Source"]=null);
RejectChange("non-boolean required flag",d=>d["Bones"]![0]!["Required"]="true");
RejectChange("missing required flag",d=>d["Bones"]![0]!.AsObject().Remove("Required"));
RejectChange("no required bones",d=>{foreach(var bone in d["Bones"]!.AsArray())bone!["Required"]=false;});
RejectChange("required child of optional parent",d=>d["Bones"]!.AsArray().Single(b=>b!["Path"]!.GetValue<string>()=="Armature/Hips/Spine/Chest")!["Required"]=false);
RejectChange("missing parent",d=>
{
    var entries=d["Bones"]!.AsArray();
    int index=entries.Select((b,i)=>(b,i)).Single(p=>p.b!["Path"]!.GetValue<string>()=="Armature/Hips/Spine").i;
    entries.RemoveAt(index);
});
RejectChange("unlisted fallback dependency",d=>
{
    var entry=d["Bones"]!.AsArray().First(b=>b!["Fallback"]!=null)!;
    entry["Fallback"]!["WhenMissing"]="Armature/Hips/UnlistedHelper";
});
RejectChange("empty fallback dependency",d=>d["Bones"]!.AsArray().First(b=>b!["Fallback"]!=null)!["Fallback"]!["WhenMissing"]="");
RejectChange("empty fallback source",d=>d["Bones"]!.AsArray().First(b=>b!["Fallback"]!=null)!["Fallback"]!["Source"]="");
RejectChange("missing fallback source",d=>d["Bones"]!.AsArray().First(b=>b!["Fallback"]!=null)!["Fallback"]!.AsObject().Remove("Source"));
RejectChange("fallback depends on itself",d=>
{
    var entry=d["Bones"]!.AsArray().First(b=>b!["Fallback"]!=null)!;
    entry["Fallback"]!["WhenMissing"]=entry["Path"]!.GetValue<string>();
});
RejectChange("fallback is not an object",d=>d["Bones"]!.AsArray().First(b=>b!["Fallback"]!=null)!["Fallback"]=new JsonArray());

var load=typeof(MotionRig).GetMethod("Load",BindingFlags.NonPublic|BindingFlags.Static)
    ??throw new InvalidOperationException("Production preset loader was not found");

// Only this validation output's copied file is changed, and its exact bytes
// are restored even if a cache assertion fails. The repository source stays intact.
try
{
    File.WriteAllText(file,"{",new UTF8Encoding(false));
    MotionRig.Initialize();
    Check(MotionRig.BonePaths().SequenceEqual(sourcePaths),"File changes after initialization cannot change the in-memory supported preset");
    Check(MotionRig.RequiredBonePaths().SequenceEqual(requiredPaths),"File changes after initialization cannot change required ownership");
    foreach(var bone in bones)
    {
        string path=bone!["Path"]!.GetValue<string>();
        Check(MotionRig.MmdSource(path,sourceSet)==bone["Source"]!.GetValue<string>(),"Cached MMD mappings survive a changed output file: "+path);
    }
    bool pathReported=false;
    try{load.Invoke(null,null);}
    catch(TargetInvocationException error) when(error.InnerException is InvalidDataException wrapped)
    { pathReported=wrapped.Message.Contains(file,StringComparison.Ordinal)&&wrapped.InnerException is JsonException; }
    Check(pathReported,"An explicit load failure identifies the malformed preset file and retains its parse cause");
}
finally { File.WriteAllBytes(file,original); }
try
{
    File.Delete(file);
    bool pathReported=false;
    try{load.Invoke(null,null);}
    catch(TargetInvocationException error) when(error.InnerException is InvalidDataException wrapped)
    { pathReported=wrapped.Message.Contains(file,StringComparison.Ordinal)&&wrapped.InnerException is FileNotFoundException; }
    Check(pathReported,"A missing startup preset reports its exact DLL-relative path and file-not-found cause");
    Check(MotionRig.BonePaths().Count()==80,"An initialized preset remains available even if its output file is later removed");
}
finally { File.WriteAllBytes(file,original); }
Check(File.ReadAllBytes(file).SequenceEqual(original),"The copied validation resource is restored byte for byte");
Console.WriteLine($"PASS {assertions} assertions: actual shared body preset, concurrent lazy initialization, 80/49 paths, complete and missing-helper MMD mappings, parser rejection and immutable process cache.");
