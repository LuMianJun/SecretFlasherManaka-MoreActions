using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SFM.Mmd.Upstream;

// A managed view of an already-owned hidden game-axis sampling hierarchy.
// It provides the exact subset of Animator/Avatar used by the upstream body
// solve, without creating a MonoBehaviour or touching the live game skeleton.
internal sealed class UpstreamAvatar
{
    readonly IReadOnlyDictionary<HumanBodyBones,Transform> bones;
    internal Transform transform { get; }
    internal bool isHuman => true;
    internal AvatarDescription avatar { get; }

    internal UpstreamAvatar(Transform hiddenRoot,IReadOnlyDictionary<HumanBodyBones,Transform> bones)
    {
        transform=hiddenRoot??throw new ArgumentNullException(nameof(hiddenRoot));
        this.bones=bones??throw new ArgumentNullException(nameof(bones));
        avatar=new AvatarDescription(Descendants(hiddenRoot).Select(node=>new SkeletonPose(node)).ToArray());
    }

    internal Transform GetBoneTransform(HumanBodyBones bone)
        =>bones.TryGetValue(bone,out var node)?node:null;

    internal static IEnumerable<Transform> Descendants(Transform root)
    {
        yield return root;
        for(int i=0;i<root.childCount;i++)
            foreach(var node in Descendants(root.GetChild(i)))yield return node;
    }

    internal sealed class AvatarDescription
    {
        internal HumanDescription humanDescription { get; }
        internal AvatarDescription(SkeletonPose[] skeleton){humanDescription=new HumanDescription(skeleton);}
    }
    internal sealed class HumanDescription
    {
        internal SkeletonPose[] skeleton { get; }
        internal HumanDescription(SkeletonPose[] skeleton){this.skeleton=skeleton;}
    }
    internal readonly struct SkeletonPose
    {
        internal readonly string name;
        internal readonly Vector3 position;
        internal readonly Quaternion rotation;
        internal SkeletonPose(Transform node){name=node.name;position=node.localPosition;rotation=node.localRotation;}
    }
}
