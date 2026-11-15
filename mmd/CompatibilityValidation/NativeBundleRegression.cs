using System.Text.Json;
using SFM.Actions;
using SFM.Mmd;
using UnityEngine;

// Raw original mesh bind matrices are inputs; independently extracted model
// P/Q are expectations. No second solver defines these geometry answers.
internal static class NativeBundleRegression
{
    const string Hips="Armature/Hips";
    readonly record struct State(Transform Bone,Vector3 Position,Quaternion Rotation,Vector3 Scale)
    {
        internal State(Transform bone):this(bone,bone.localPosition,bone.localRotation,bone.localScale){}
        internal bool Matches()=>Vector3.Distance(Bone.localPosition,Position)<.0001f
            &&Quaternion.Angle(Bone.localRotation,Rotation)<.1f&&Vector3.Distance(Bone.localScale,Scale)<.00001f;
    }

    internal static void Run(Action<bool,string> check)
    {
        using var document=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","native-model-bind.json")));
        var fixture=document.RootElement;
        check(fixture.GetProperty("source").GetProperty("sha256").GetString()=="98621b2c4fa4f9de80f355fae5776ab17efc016bcc81c581bcd0917af4308367",
            "Native fixture identifies the exact original model bundle");
        var rows=fixture.GetProperty("bones").EnumerateArray().ToArray();
        var binds=rows.Where(row=>row.GetProperty("bind").GetBoolean()).ToDictionary(row=>row.GetProperty("path").GetString());
        check(rows.Length==80&&binds.Count==78,"The original bundle contains all 80 preset paths and 78 mesh bind bones");
        float largestScaleError=binds.Values.Max(row=>row.GetProperty("scale").EnumerateArray().Max(value=>MathF.Abs(value.GetSingle()-1)));
        check(largestScaleError<.000002f,"Original model bind scales are unity within extraction/serialization precision");
        var meshRows=fixture.GetProperty("renderers").EnumerateArray().ToArray();
        var body=meshRows.Single(row=>row.GetProperty("path").GetString()=="Body.001");
        check(V(body,"position").magnitude<.00001f&&Vector3.Distance(V(body,"scale"),Vector3.one)<.00001f
            &&Quaternion.Angle(Q(body,"rotation"),Quaternion.AngleAxis(-90,Vector3.right))<.1f,
            "The original Body.001 mesh frame is X(-90 degrees), rather than identity");
        foreach(var renderer in meshRows)
        {
            var meshMatrix=Matrix4x4.TRS(V(renderer,"position"),Q(renderer,"rotation"),V(renderer,"scale"));
            foreach(var bone in renderer.GetProperty("bones").EnumerateArray())
            {
                string path=bone.GetProperty("path").GetString();
                var world=meshMatrix*Matrix(bone.GetProperty("matrix")).inverse;
                check(Vector3.Distance(world.GetColumn(3),V(binds[path],"position"))<.00001f,
                    "Each original renderer independently reconstructs the same native bind point: "+renderer.GetProperty("path").GetString()+" "+path);
                check(Quaternion.Angle(world.rotation,Q(binds[path],"rotation"))<.1f,
                    "Each original renderer independently reconstructs the same native bind axis: "+renderer.GetProperty("path").GetString()+" "+path);
            }
        }

        foreach(float height in new[]{.9f,1.125f,1.2f})
        {
            MmdRigCache.Clear();
            using var actor=new NativeActor(fixture,height);
            var snapshot=MmdTargetSnapshot.Read(actor.Root);
            var rig=MmdRigCache.Rent(actor.Root,MmdReferencePose.T);
            try
            {
                check(snapshot.Bones.Count==80&&snapshot.Rest.Count==78&&rig.bindings.Count==80,
                    "Original native hierarchy builds 80 samples from 78 mesh binds and two native identity controls");
                check(rig.bindings.Count(binding=>binding.human.HasValue)==51,
                    "Original native hierarchy has 51 currently controlled Humanoid paths");
                foreach(var pair in binds)
                {
                    var expectedP=V(pair.Value,"position")*height;
                    var expectedQ=Q(pair.Value,"rotation");
                    var sample=rig.bindings.Single(binding=>binding.path==pair.Key).sample;
                    check(Vector3.Distance(sample.position,expectedP)<.0001f,
                        "Raw native bind matrices recover the full original world geometry once: "+pair.Key);
                    check(Quaternion.Angle(sample.rotation,expectedQ)<.1f,
                        "Raw native bind matrices recover the full original world axes: "+pair.Key);
                }
            }
            finally {MmdRigCache.Return(rig);}

            var initial=actor.Bones.ToDictionary(pair=>pair.Key,pair=>(pair.Value.position,pair.Value.rotation));
            var local=actor.Bones.Values.Select(bone=>new State(bone)).ToArray();
            var nativeArmature=new State(actor.Root.transform.Find("Armature"));
            var nativeRoot=new State(actor.Root.transform);
            using(var pose=new RuntimeVmdPose(actor.Root,new ActionDefinition(),Motion(Vector3.zero),MmdReferencePose.T))
            {
                check(pose.BoneCount==51,"Building 78 native bind samples does not claim unsupported extra bone tracks");
                pose.Apply(0);
                foreach(var pair in actor.Bones)
                {
                    check(Vector3.Distance(pair.Value.position,initial[pair.Key].position)<.0001f,
                        "Zero authored motion with IK disabled preserves original native world position: "+pair.Key);
                    check(Quaternion.Angle(pair.Value.rotation,initial[pair.Key].rotation)<.1f,
                        "Zero authored motion with IK disabled preserves original native world rotation: "+pair.Key);
                }
            }
            check(local.All(state=>state.Matches()),"Native full-body zero motion restores all original locals on stop");

            var unowned=actor.Bones.Where(pair=>!GameHumanoidMap.TryGet(pair.Key,out _)).Select(pair=>new State(pair.Value)).ToArray();
            using(var pose=new RuntimeVmdPose(actor.Root,new ActionDefinition(),Motion(Vector3.one),MmdReferencePose.T))
            {
                pose.Apply(0);pose.Apply(.5f);
                check(Vector3.Distance(pose.RootDisplacement,actor.Root.transform.rotation*new Vector3(.04f,0,.04f))<.0001f,
                    "Original full-body native axes keep half a Center unit at .04 horizontal world metres");
                foreach(var pair in actor.Bones.Where(pair=>GameHumanoidMap.TryGet(pair.Key,out _)))
                {
                    check(Vector3.Distance(pair.Value.position,initial[pair.Key].position+Vector3.up*.04f)<.0001f,
                        "Original full-body native joints retain vertical Center travel in world metres: "+pair.Key);
                    check(Quaternion.Angle(pair.Value.rotation,initial[pair.Key].rotation)<.1f,
                        "Center translation does not tilt the original full-body native axes: "+pair.Key);
                }
                check(unowned.Length==29&&unowned.All(state=>state.Matches()),
                    "The 29 extra original hierarchy paths retain native local TRS during standard body output");
                check(nativeRoot.Matches()&&nativeArmature.Matches(),"Original root and Armature stay owned by the game");
            }
            check(local.All(state=>state.Matches()),"Stopping Center output restores all original native locals");
            if(height==1.125f)
            {
                var defaultIk=Motion(Vector3.zero);defaultIk.RawIkFrames.Clear();
                using var pose=new RuntimeVmdPose(actor.Root,new ActionDefinition(),defaultIk,MmdReferencePose.T);
                pose.Apply(0);
                var changes=actor.Bones.Where(pair=>GameHumanoidMap.TryGet(pair.Key,out _)).Select(pair=>
                    (path:pair.Key,p:Vector3.Distance(pair.Value.position,initial[pair.Key].position),q:Quaternion.Angle(pair.Value.rotation,initial[pair.Key].rotation)))
                    .Where(value=>value.p>.0001f||value.q>.1f).ToArray();
                check(changes.All(value=>float.IsFinite(value.p)&&float.IsFinite(value.q)),"Original full-body default IK keeps finite coordinates");
                Console.WriteLine("NATIVE_BUNDLE_DEFAULT_IK "+string.Join(";",changes.Select(value=>value.path+":p="+value.p+",q="+value.q)));
            }
        }
        Console.WriteLine("NATIVE_BUNDLE_BIND paths=80 meshBinds=78 samples=80 controlledHumanoid=51 maxScaleError="+largestScaleError);
        MmdRigCache.Clear();
    }

    static MmdMotion Motion(Vector3 travel)
    {
        var motion=new MmdMotion{lastFrame=30};
        motion.tracks["センター"]=[Key(0,Vector3.zero),Key(30,travel)];
        motion.RawIkFrames.Add((0,true,new(){new("左足ＩＫ",false),new("右足ＩＫ",false),
            new("左つま先ＩＫ",false),new("右つま先ＩＫ",false)}));
        return motion;
    }
    static MmdMotion.Key Key(uint frame,Vector3 position)
    {
        var curve=new byte[64];
        for(int channel=0;channel<4;channel++)
        {curve[channel]=20;curve[channel+4]=20;curve[channel+8]=107;curve[channel+12]=107;}
        return new(){frame=frame,p=position,q=Quaternion.identity,interpolation=curve};
    }
    static Vector3 V(JsonElement row,string name)
    {var v=row.GetProperty(name);return new(v[0].GetSingle(),v[1].GetSingle(),v[2].GetSingle());}
    static Quaternion Q(JsonElement row,string name)
    {var q=row.GetProperty(name);return new(q[0].GetSingle(),q[1].GetSingle(),q[2].GetSingle(),q[3].GetSingle());}
    static Matrix4x4 Matrix(JsonElement values)
    {
        // The source stores Unity's row/column matrix. The math boundary uses
        // System.Numerics row vectors internally, hence this storage transpose.
        float F(int n)=>values[n].GetSingle();
        return new(new System.Numerics.Matrix4x4(F(0),F(4),F(8),F(12),F(1),F(5),F(9),F(13),
            F(2),F(6),F(10),F(14),F(3),F(7),F(11),F(15)));
    }

    internal sealed class NativeActor:IDisposable
    {
        internal readonly GameObject Root=new("OriginalBundleAvatar");
        internal readonly Dictionary<string,Transform> Bones=new();
        internal NativeActor(JsonElement fixture,float height)
        {
            Root.transform.position=new(3.5f,-.035f,-2.25f);
            Root.transform.rotation=Quaternion.AngleAxis(73,Vector3.up);
            Root.transform.localScale=Vector3.one*height;
            var armature=new GameObject("Armature").transform;armature.SetParent(Root.transform,false);
            SetLocal(armature,fixture.GetProperty("armature"));
            foreach(var row in fixture.GetProperty("bones").EnumerateArray().OrderBy(row=>row.GetProperty("path").GetString().Count(c=>c=='/')))
            {
                string path=row.GetProperty("path").GetString();int slash=path.LastIndexOf('/');
                var bone=new GameObject(path[(slash+1)..]).transform;
                bone.SetParent(Root.transform.Find(path[..slash]),false);
                if(row.GetProperty("bind").GetBoolean())
                {
                    bone.position=Root.transform.position+Root.transform.rotation*(V(row,"position")*height);
                    bone.rotation=Root.transform.rotation*Q(row,"rotation");
                }
                else SetLocal(bone,row.GetProperty("local"));
                Bones.Add(path,bone);
            }
            foreach(var row in fixture.GetProperty("renderers").EnumerateArray())
            {
                var meshNode=new GameObject(row.GetProperty("path").GetString());meshNode.transform.SetParent(Root.transform,false);
                SetLocal(meshNode.transform,row);
                var renderer=meshNode.AddComponent<SkinnedMeshRenderer>();
                var binds=row.GetProperty("bones").EnumerateArray().ToArray();
                renderer.bones=binds.Select(bind=>Bones[bind.GetProperty("path").GetString()]).ToArray();
                var names=Enumerable.Range(0,FixedModelFace.ShapeCount).Select(i=>"shape"+i).ToArray();
                names[0]="vrc.v_aa";names[19]="eye_close";
                foreach(var entry in FixedModelFace.MmdMorphs)names[entry.Index]=entry.Name;
                renderer.sharedMesh=new(){ShapeNames=names,bindposes=binds.Select(bind=>Matrix(bind.GetProperty("matrix"))).ToArray()};
            }
        }
        static void SetLocal(Transform node,JsonElement row)
        {node.localPosition=V(row,"position");node.localRotation=Q(row,"rotation");node.localScale=V(row,"scale");}
        public void Dispose()=>UnityEngine.Object.Destroy(Root);
    }
}
