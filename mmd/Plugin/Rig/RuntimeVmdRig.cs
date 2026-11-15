using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using SFM.Actions;
using SFM.Mmd.Upstream;

namespace SFM.Mmd;

// A private native-axis avatar. The upstream solver owns this tree; the game
// receives its solved pose through the existing native-body lease only.
internal sealed class RuntimeVmdRig
{
    internal sealed class Binding
    {
        internal string path,source,presetSource;
        internal Transform live,sample;
        internal Binding parent;
        internal HumanBodyBones? human;
    }
    internal readonly List<Binding> bindings=new();
    internal UpstreamAvatar avatar;
    internal SkinnedMeshRenderer faceRenderer;
    internal GameObject target,proxy;
    internal Transform liveRoot,hip,sampleHip;
    internal float[] Shape;
    internal string ShapePaths;
    internal Vector3 ReferenceHipPosition;
    internal Vector3 ControlWaistPivot;
    internal bool Leased,Retired,DetachedOnReturn,GeometryInvalidated;
    internal MmdReferencePose ReferencePose;
    internal string SkeletonKey=>MmdRigCache.SkeletonKey(ReferencePose);
    readonly List<(SkinnedMeshRenderer renderer,Mesh mesh)> meshes=new();
    KeyValuePair<string,string>[] aliases=Array.Empty<KeyValuePair<string,string>>();
    readonly Dictionary<MmdMotion,MmdMotion> boundMotions=new();
    readonly Queue<MmdMotion> motionOrder=new();

    internal bool IsUsable
    {
        get
        {
            if(GeometryInvalidated||target==null||liveRoot==null||hip==null||proxy==null||avatar==null)return false;
            foreach(var b in bindings)if(b.live==null||b.sample==null)return false;
            foreach(var mesh in meshes)if(mesh.renderer==null||mesh.renderer.sharedMesh!=mesh.mesh)return false;
            return true;
        }
    }
    internal bool HelpersValid=>proxy!=null&&avatar!=null;
    internal void Rebind(MmdTargetSnapshot snapshot)
    {
        target=snapshot.Target;liveRoot=snapshot.Root;hip=snapshot.Bones["Armature/Hips"];
        foreach(var b in bindings)b.live=snapshot.Bones[b.path];
        meshes.Clear();meshes.AddRange(snapshot.Meshes);faceRenderer=snapshot.Face;
        Shape=snapshot.Shape;ShapePaths=snapshot.ShapePaths;DetachedOnReturn=false;
    }
    internal void Detach()
    {
        target=null;liveRoot=null;hip=null;meshes.Clear();faceRenderer=null;
        foreach(var b in bindings)b.live=null;
    }
    internal IEnumerable<int> BuildSteps(MmdTargetSnapshot snapshot,MmdReferencePose referencePose=MmdReferencePose.A)
    {
        ReferencePose=referencePose;Rebind(snapshot);
        proxy=new GameObject("MmdUpstreamSamplingAvatar");proxy.hideFlags=HideFlags.HideAndDontSave;proxy.SetActive(false);
        UnityEngine.Object.DontDestroyOnLoad(proxy);
        // Positions already contain the native metric. Upstream resets its
        // center ghost's LOCAL rotation and adds VMD offsets in its parent
        // space, so that control space must be upright and measured in metres.
        // Native Armature rotation/scale remain on the live actor only; the
        // children's measured world rest positions and axes are retained below.
        yield return 0;
        var armature=new GameObject("Armature").transform;armature.SetParent(proxy.transform,false);
        var paths=new Dictionary<string,Binding>(StringComparer.Ordinal);
        var humans=new Dictionary<HumanBodyBones,Transform>();
        var available=snapshot.Bones.Keys.ToHashSet(StringComparer.Ordinal);
        foreach(var path in available.OrderBy(p=>p.Count(c=>c=='/')).ThenBy(p=>p,StringComparer.Ordinal))
        {
            var sample=new GameObject(path.Substring(path.LastIndexOf('/')+1)).transform;
            string parentPath=path.Substring(0,path.LastIndexOf('/'));Binding parent=null;
            while(parentPath.Contains('/'))
            {
                if(paths.TryGetValue(parentPath,out parent))break;
                parentPath=parentPath.Substring(0,parentPath.LastIndexOf('/'));
            }
            sample.SetParent(parent?.sample??armature,false);
            if(snapshot.Rest.TryGetValue(path,out var rest))
            {
                var metricPosition=snapshot.Positions[path];sample.position=new Vector3(metricPosition.X,metricPosition.Y,metricPosition.Z);
                sample.rotation=rest.rotation;
            }
            // The preset's two BreastRotate intermediary nodes have no mesh
            // bind. Their original local TRS is identity; live animation and
            // customization are retained by the native delta output adapter.
            var binding=new Binding{path=path,live=snapshot.Bones[path],sample=sample,parent=parent,
                presetSource=MotionRig.MmdSource(path,available)};
            if(GameHumanoidMap.TryGet(path,out var human))
            {
                binding.human=human;humans.Add(human,sample);
                if(UpstreamBodySolver.StandardMapping.TryGetValue(human,out var source))binding.source=source.ToString();
            }
            bindings.Add(binding);paths.Add(path,binding);yield return 0;
        }
        sampleHip=humans[HumanBodyBones.Hips];ReferenceHipPosition=sampleHip.position;
        // Target-character semi-standard waist convention. VMD contains no
        // source-model static pivot, so derive it once from measured geometry.
        var thighs=(humans[HumanBodyBones.LeftUpperLeg].position+humans[HumanBodyBones.RightUpperLeg].position)*.5f;
        ControlWaistPivot=new Vector3(thighs.x,Mathf.Lerp(ReferenceHipPosition.y,thighs.y,.6f),-ReferenceHipPosition.y*.02f);
        avatar=new UpstreamAvatar(proxy.transform,humans);
        aliases=CreateAliases().ToArray();
        yield return 0;
    }
    IEnumerable<KeyValuePair<string,string>> CreateAliases()
    {
        var encoding=System.Text.Encoding.GetEncoding(932);
        foreach(var b in bindings)
        {
            // Toe/native thumb leaf tracks stay independent of semantic
            // EX/zero-index thumb controls. Standard aliases keep priority.
            string source=b.source??(!b.human.HasValue?b.presetSource:null);
            if(source==null)continue;
            yield return new(b.sample.name,source);
            var bytes=encoding.GetBytes(b.sample.name);
            if(bytes.Length>15)yield return new(encoding.GetString(bytes,0,15),source);
            bytes=encoding.GetBytes(source);
            if(bytes.Length>15)yield return new(encoding.GetString(bytes,0,15),source);
        }
    }
    internal IEnumerable<KeyValuePair<string,string>> Aliases()=>aliases;
    internal MmdMotion BindMotion(MmdMotion keys)
    {
        if(!keys.PreparedForPlayback)return UpstreamInputCompatibility.Apply(keys).BindAliases(aliases);
        if(boundMotions.TryGetValue(keys,out var cached))return cached;
        var bound=UpstreamInputCompatibility.Apply(keys).BindAliases(aliases);
        if(boundMotions.Count>=16)boundMotions.Remove(motionOrder.Dequeue());
        boundMotions.Add(keys,bound);motionOrder.Enqueue(keys);return bound;
    }
    internal void Destroy()
    {
        boundMotions.Clear();motionOrder.Clear();aliases=Array.Empty<KeyValuePair<string,string>>();
        avatar=null;
        if(proxy!=null)UnityEngine.Object.Destroy(proxy);proxy=null;
    }
}
