using System;
using System.Collections.Generic;
using UnityEngine;

namespace Seleka2.Actions;

// Fixed base-model channel layout, verified against the original Player /
// Manustia Body.001 export. Only names/indices are included, no model assets.
public static class FixedModelFace
{
    public const int ShapeCount = 473;
    public const string FacePath = "Body.001";
    public static IReadOnlyList<(int Index,string Name)> MmdMorphs { get; } = new (int,string)[]
    {
        (425, "まばたき"),
        (426, "笑い"),
        (427, "ウィンク"),
        (428, "ウィンク右"),
        (429, "ウィンク２"),
        (430, "ｳｨﾝｸ２右"),
        (431, "なごみ"),
        (432, "はぅ"),
        (433, "びっくり"),
        (434, "じと目"),
        (435, "ｷﾘｯ"),
        (436, "はちゅ目"),
        (437, "星目"),
        (438, "はぁと"),
        (439, "瞳小"),
        (440, "瞳大"),
        (441, "恐ろしい子！"),
        (442, "ハイライト消し"),
        (443, "あ"),
        (444, "い"),
        (445, "えー"),
        (446, "う"),
        (447, "え"),
        (448, "お"),
        (449, "ワ"),
        (450, "ω"),
        (451, "ω□"),
        (452, "∧"),
        (453, "▲"),
        (454, "はんっ！"),
        (455, "にやり"),
        (456, "にっこり"),
        (457, "ぺろっ"),
        (458, "てへぺろ"),
        (459, "てへぺろ２"),
        (460, "口角上げ"),
        (461, "口角下げ"),
        (462, "口横広げ"),
        (463, "真面目"),
        (464, "困る"),
        (465, "にこり"),
        (466, "怒り"),
        (467, "上"),
        (468, "下"),
        (469, "頬染め"),
        (470, "がーん"),
        (471, "青ざめ"),
        (472, "涙"),
    };
    public static IReadOnlyList<int> NativeExpressions { get; } = new int[] { 0, 3, 5, 8, 9, 15, 19, 20, 21, 25, 52, 53, 54, 65, 67, 70, 73, 74, 75, 76, 77, 78, 79, 80, 81, 84, 85, 87, 88, 92, 94, 95, 115, 116, 117, 118, 119, 120, 121, 122, 124, 125, 126, 130, 131, 132, 133, 134, 135, 142, 144, 201, 203, 205, 206, 207, 209, 210, 242, 243, 244, 245, 247, 250, 252, 254, 256, 258, 260, 261, 262, 264, 265, 268, 270, 273, 275, 279, 281, 282, 284, 287, 288, 289, 290, 291, 292, 293, 296, 298, 301, 302, 310, 316, 318, 319, 330, 331, 332, 333, 336, 338, 339, 340, 342, 348, 354, 357, 359, 360, 361, 362, 366, 367, 381, 384, 385, 408, 409, 410, 415, 416, 417, 418, 419, 420, 421, 422, 423, 471 };
    static readonly Dictionary<int,(Mesh Mesh,bool Valid)> layouts = new();

    public static SkinnedMeshRenderer Find(GameObject target)
    {
        if(target==null)return null;
        var node=target.transform.Find(FacePath);
        var face=node==null?null:node.GetComponent<SkinnedMeshRenderer>();
        return face!=null&&Validate(face.sharedMesh)?face:null;
    }
    // Validate the declared channel slots once per loaded mesh; never discover
    // channels, inspect unrelated renderers or scan the character hierarchy.
    public static bool Validate(Mesh mesh)
    {
        if(mesh==null)return false;
        int id=mesh.GetInstanceID();
        if(layouts.TryGetValue(id,out var cached)&&cached.Mesh==mesh)return cached.Valid;
        bool valid=mesh.blendShapeCount==ShapeCount;
        if(valid)valid=mesh.GetBlendShapeName(0)=="vrc.v_aa"&&mesh.GetBlendShapeName(19)=="eye_close";
        if(valid)foreach(var entry in MmdMorphs)
            if(mesh.GetBlendShapeName(entry.Index)!=entry.Name){valid=false;break;}
        if(layouts.Count>16)
        {
            var dead=new List<int>();foreach(var pair in layouts)if(pair.Value.Mesh==null)dead.Add(pair.Key);
            foreach(int key in dead)layouts.Remove(key);
        }
        layouts[id]=(mesh,valid);
        if(!valid)Plugin.LogSource?.LogWarning("FIXED_FACE_LAYOUT_MISMATCH expected=Body.001/473; face animation disabled for this mesh");
        return valid;
    }
}
