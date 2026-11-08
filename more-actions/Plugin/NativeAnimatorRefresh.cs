using System;
using System.Collections.Generic;
using UnityEngine;

namespace Seleka2.Actions;

// A controller/clip-binding change needs one native pose evaluation before
// native additive body/camera corrections run. Same-base override changes
// preserve state natively; replaying every layer also replays time-driven or
// empty layers, whose normalized time can legitimately be nonfinite.
internal sealed class NativeAnimatorRefresh : IDisposable
{
    readonly Animator animator;
    readonly float[] weights;
    readonly AnimatorCullingMode culling;
    readonly bool canEvaluate;
    readonly List<(SkinnedMeshRenderer Renderer,Mesh Mesh,float[] Values)> shapes=new();
    RuntimeAnimatorController expected;
    bool touched,evaluated,disposed;

    internal NativeAnimatorRefresh(Animator target)
    {
        animator=target;
        canEvaluate=target.enabled&&target.isInitialized&&target.gameObject.activeInHierarchy;
        culling=target.cullingMode;
        weights=new float[target.layerCount];
        for(int i=0;i<weights.Length;i++)weights[i]=target.GetLayerWeight(i);
        // The game has one fixed base model. Preserve its appearance without
        // discovering renderers, reading morph names or scanning accessories.
        foreach(string path in new[]{"Body.001","Selestia_body"})
        {
            var node=target.transform.Find(path);
            var renderer=node==null?null:node.GetComponent<SkinnedMeshRenderer>();
            var mesh=renderer==null?null:renderer.sharedMesh;
            if(mesh==null||mesh.blendShapeCount==0)continue;
            var values=new float[mesh.blendShapeCount];
            for(int i=0;i<values.Length;i++)values[i]=renderer.GetBlendShapeWeight(i);
            shapes.Add((renderer,mesh,values));
        }
    }

    internal void Evaluate(RuntimeAnimatorController controller)
    {
        if(disposed)throw new ObjectDisposedException(nameof(NativeAnimatorRefresh));
        if(evaluated)return;
        expected=controller;
        touched=true;
        if(animator.runtimeAnimatorController!=controller)animator.runtimeAnimatorController=controller;
        if(!canEvaluate)return;
        animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
        evaluated=true;
        // Native Unity entry. Do not call Rebind, reset parameters, replay
        // states, or write a camera/marker transform to hide stale bindings.
        animator.Update(0f);
    }

    public void Dispose()
    {
        if(disposed)return;disposed=true;
        if(animator==null)return;
        try
        {
            if(touched&&animator.runtimeAnimatorController==expected)
            {
                for(int i=0;i<weights.Length&&i<animator.layerCount;i++)animator.SetLayerWeight(i,weights[i]);
                foreach(var shape in shapes)
                    if(shape.Renderer!=null&&shape.Renderer.sharedMesh==shape.Mesh)
                        for(int i=0;i<shape.Values.Length;i++)shape.Renderer.SetBlendShapeWeight(i,shape.Values[i]);
            }
        }
        finally
        {
            if(evaluated&&animator.cullingMode==AnimatorCullingMode.AlwaysAnimate)animator.cullingMode=culling;
        }
    }
}
