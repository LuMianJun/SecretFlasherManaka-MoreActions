namespace Seleka2.Actions;
using System;
using System.Collections.Generic;
using UnityEngine;

public static class MotionSkeleton
{
    // Native PlayerBoneScaleAdjustManager inserts these identity-rotation
    // wrappers one frame after player initialization. They are not clip bones.
    static bool IsAdjuster(Transform node) => node.name == "PlayerBoneScaleAdjuster";

    public static string CanonicalPath(Transform node, Transform root)
    {
        var parts = new List<string>();
        while (node != root)
        {
            if (node == null) throw new Exception("Bone is outside avatar hierarchy");
            if (!IsAdjuster(node)) parts.Add(node.name);
            node = node.parent;
        }
        parts.Reverse();
        return string.Join("/", parts);
    }

    public static Dictionary<string, Transform> ResolveBones(Transform root)
    {
        var required = new HashSet<string>(MotionRig.BonePaths());
        var resolved = new Dictionary<string, Transform>();
        foreach (var node in root.GetComponentsInChildren<Transform>(true))
        {
            if (IsAdjuster(node)) continue;
            var path = CanonicalPath(node, root);
            if (!required.Contains(path)) continue;
            if (resolved.ContainsKey(path)) throw new Exception("Ambiguous body bone: " + path);
            resolved.Add(path, node);
        }
        foreach (var path in required)
            if (!resolved.ContainsKey(path)) throw new Exception("Required live body bone missing: " + path);
        return resolved;
    }

    public static void CopyCanonicalTree(Transform source, Transform destination)
    {
        for (int i = 0; i < source.childCount; i++)
        {
            var child = source.GetChild(i);
            if (IsAdjuster(child))
            {
                // Only the native scale wrapper is omitted; ordinary accessory
                // ancestors remain, so similarly named accessory bones cannot bind.
                if (Quaternion.Angle(child.localRotation, Quaternion.identity) > 0.01f || child.localPosition.sqrMagnitude > 0.000001f)
                    throw new Exception("Unexpected body adjuster translation/rotation");
                CopyCanonicalTree(child, destination);
                continue;
            }
            var copy = new GameObject(child.name).transform;
            copy.SetParent(destination, false);
            copy.localPosition = child.localPosition;
            copy.localRotation = child.localRotation;
            copy.localScale = child.localScale;
            CopyCanonicalTree(child, copy);
        }
    }
}
