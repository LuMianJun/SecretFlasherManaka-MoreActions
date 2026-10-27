using System;
using System.Collections.Generic;
using UnityEngine;

namespace Seleka2.Actions;

public static class ActionFaceSettings
{
    // The settings UI updates this in the same session; pack=false is a hard veto.
    public static bool KeepGameExpressions { get; internal set; } = true;
}

// Per-pose transient overlay, shared by VMD and Generic clips. Never changes
// the native face state machine, animator parameters, appearance or save data.
public sealed class ActionFaceMorphLayer
{
    // Native *Face*.anim: Body.001 path hash 4275954259, type 137, customType 20.
    // 122 unique original blend-shape name hashes from 26 face clips.
    static readonly HashSet<uint> expressionHashes = new()
    {
        1011931547u, 1031805641u, 10325211u, 1073796401u, 1098283681u, 110357521u, 1120506286u, 1174129174u,
        1189104974u, 1191585397u, 1202680751u, 1230192683u, 1271975257u, 1282725604u, 1287724005u, 1314232825u,
        1334363075u, 1357361504u, 1414385679u, 1422586971u, 1454768390u, 1528327578u, 163437014u, 1675244332u,
        1773927912u, 1784880274u, 1805468892u, 184276781u, 1868403130u, 1892219861u, 1904161561u, 1953247175u,
        198554507u, 2012200459u, 2187831256u, 2211778967u, 2264020638u, 2270661043u, 2271926272u, 2292322775u,
        2301925389u, 231220243u, 2339376762u, 2373652666u, 2424788897u, 2463949361u, 2529705257u, 2557534466u,
        2677718443u, 2693863159u, 2694866322u, 2719162900u, 2822923747u, 2867366915u, 2897820773u, 295268152u,
        296435821u, 2996303175u, 3016415040u, 3044387308u, 305954237u, 3100028109u, 3121604690u, 3229318615u,
        3274465388u, 3378124086u, 340333919u, 3434966640u, 3450539687u, 348663091u, 3497470655u, 3518976282u,
        3522323176u, 3570638480u, 3590021074u, 3680176028u, 3708467194u, 3709790114u, 3734104642u, 3736512996u,
        3759126671u, 3818356229u, 3853592206u, 3856388775u, 3902639421u, 3903383685u, 398308400u, 3994416849u,
        4006023248u, 4139400052u, 415214147u, 4180907542u, 4194482321u, 4198688158u, 4247433322u, 4284590398u,
        443854175u, 444105534u, 445816173u, 455424947u, 457585441u, 460256866u, 499471609u, 503273844u,
        521649392u, 532552685u, 54307701u, 592109721u, 613414906u, 652896881u, 685869273u, 71248087u,
        804545201u, 861218901u, 862108054u, 886258u, 895626974u, 91133021u, 917469895u, 917656339u,
        993777840u, 997451378u,
    };
    // Original PlayerFaceController.OnChangeBody appearance writes. Some eye
    // channels also receive expression curves; keep the custom face intact.
    static readonly HashSet<int> appearance = new() { 55,64,147,148,149,161,170,199,200,312,313,363,364,365,375,376,379,380,388,392,393,394,395,400,401,414 };
    static readonly HashSet<int> nativeLate = new() { 15,205,209,242,252,261,290,291,292,301,318,319,408,410,415,416,417,418,419 };
    static readonly string[] mapped = { "eye_close", "vrc.v_aa", "vrc.v_ih", "vrc.v_ou", "vrc.v_e", "vrc.v_oh" };
    sealed class Channel
    {
        internal SkinnedMeshRenderer Renderer;
        internal int Index;
        internal float Native, Output;
    }
    readonly ActionDefinition action;
    readonly List<Channel> channels = new();
    readonly Dictionary<(int renderer,int index),Channel> byIndex = new();
    bool applied, overlay;

    public ActionFaceMorphLayer(GameObject target, ActionDefinition definition)
    {
        action = definition;
        if (!action.WriteFaceMorphs || target == null) return;
        foreach (var renderer in target.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (renderer.name != "Body.001" || renderer.sharedMesh == null) continue;
            var mesh = renderer.sharedMesh;
            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                string name = mesh.GetBlendShapeName(i);
                bool canonical = mesh.blendShapeCount >= 420;
                bool expression = expressionHashes.Contains(unchecked((uint)Animator.StringToHash(name))) || (canonical && nativeLate.Contains(i));
                if (Array.IndexOf(mapped, name) < 0 && (!expression || (canonical && appearance.Contains(i)))) continue;
                var channel = new Channel { Renderer=renderer, Index=i };
                channels.Add(channel); byIndex.Add((renderer.GetInstanceID(),i),channel);
            }
        }
    }

    public void Begin()
    {
        RestoreNativeFace();
        overlay = action.KeepGameExpressions && ActionFaceSettings.KeepGameExpressions;
        foreach (var channel in channels)
        {
            if (channel.Renderer == null) continue;
            channel.Native = channel.Renderer.GetBlendShapeWeight(channel.Index);
            channel.Output = overlay ? channel.Native : 0;
            if (!overlay) channel.Renderer.SetBlendShapeWeight(channel.Index,0);
        }
        applied = true;
    }

    public void Write(SkinnedMeshRenderer renderer, int index, float motionWeight)
    {
        if (!applied || renderer == null || !byIndex.TryGetValue((renderer.GetInstanceID(),index),out var channel)) return;
        channel.Output = Mathf.Clamp((overlay ? channel.Native : 0) + motionWeight,0,100);
        renderer.SetBlendShapeWeight(index,channel.Output);
    }

    public void RestoreNativeFace()
    {
        if (!applied) return;
        applied = false;
        foreach (var channel in channels)
        {
            // Preserve any newer native write made after our last overlay.
            if (channel.Renderer != null && channel.Renderer.GetBlendShapeWeight(channel.Index) == channel.Output)
                channel.Renderer.SetBlendShapeWeight(channel.Index,channel.Native);
        }
    }
}
