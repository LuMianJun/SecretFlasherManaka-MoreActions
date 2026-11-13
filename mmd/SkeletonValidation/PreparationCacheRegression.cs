using System.Diagnostics;
using System.Reflection;
using System.Text;
using SFM.Actions;
using SFM.Mmd;
using UnityEngine;

internal static class PreparationCacheRegression
{
    internal static void Run(Action<bool,string> check,
        Func<(GameObject avatar,Dictionary<string,Transform> bones,SkinnedMeshRenderer renderer)> createAvatar)
    {
        var snapshotField=typeof(MmdRigPreparation).GetField("snapshot",BindingFlags.Instance|BindingFlags.NonPublic)
            ??throw new Exception("Preparation snapshot field unavailable");
        var previousActions=ActionRegistry.Registered;
        int previousRevision=ActionRegistry.Revision;
        var previousTarget=ActionTargetApi.Target;
        bool previousReady=ActionTargetApi.IsReady;
        string folder=Path.Combine(Path.GetTempPath(),"sfm-preparation-cache-"+Guid.NewGuid().ToString("N"));
        var actors=new List<GameObject>();
        MmdRigPreparation pump=null;
        Directory.CreateDirectory(folder);
        try
        {
            MmdRigCache.Clear();
            ActionRegistry.Registered=new(StringComparer.Ordinal);
            ActionRegistry.Revision++;
            string path=Path.Combine(folder,"motion.vmd");WriteMotion(path);
            var sourceA=new RuntimeVmdSource(path,MmdReferencePose.A);
            var sourceT=new RuntimeVmdSource(path,MmdReferencePose.T);
            var actor=createAvatar();actors.Add(actor.avatar);
            ActionTargetApi.Target=actor.avatar;ActionTargetApi.IsReady=true;
            pump=new MmdRigPreparation(IntPtr.Zero);
            ActionRegistry.RegisterRuntime(new(){Id="preparation-a"},sourceA,folder,"fixture");

            void Await(Func<bool> ready,string label)
            {
                var deadline=Stopwatch.StartNew();
                while(!ready())
                {
                    pump.Update();
                    if(deadline.Elapsed.TotalSeconds>10)throw new Exception("Preparation regression timed out: "+label);
                    Thread.Sleep(1);
                }
            }
            object Snapshot()=>snapshotField.GetValue(pump)??throw new Exception("Prepared rig has no actor snapshot");
            void Tick(){for(int i=0;i<8;i++)pump.Update();}

            int before=MmdRigCache.BuildCount;
            Await(()=>sourceA.IsPrepared(actor.avatar),"initial A");
            check(MmdRigCache.BuildCount==before+1,"Preparing an A-only registry builds one rig");
            object originalSnapshot=Snapshot();
            int warmedBuilds=MmdRigCache.BuildCount;
            foreach(string change in new[]{"ring visibility","alias mapping"})
            {
                // The registry boundary represents these presentation changes
                // by the same Revision increment used by the real registry.
                ActionRegistry.Revision++;Tick();
                check(ReferenceEquals(originalSnapshot,Snapshot())&&MmdRigCache.BuildCount==warmedBuilds,
                    "A "+change+" revision preserves the actor snapshot and existing rig");
            }
            ActionRegistry.RegisterRuntime(new(){Id="preparation-another-a"},sourceA,folder,"fixture");Tick();
            check(ReferenceEquals(originalSnapshot,Snapshot())&&MmdRigCache.BuildCount==warmedBuilds,
                "Registering another motion with the same A requirement does not recapture the actor");

            ActionRegistry.RegisterRuntime(new(){Id="preparation-t"},sourceT,folder,"fixture");
            Await(()=>sourceA.IsPrepared(actor.avatar)&&sourceT.IsPrepared(actor.avatar),"add T");
            check(ReferenceEquals(originalSnapshot,Snapshot())&&MmdRigCache.BuildCount==warmedBuilds+1,
                "Adding T retains the actor snapshot and only builds the missing reference pose");
            int bothBuilds=MmdRigCache.BuildCount;
            ActionRegistry.Registered.Remove("preparation-t");ActionRegistry.Revision++;Tick();
            check(ReferenceEquals(originalSnapshot,Snapshot())&&MmdRigCache.BuildCount==bothBuilds,
                "Removing a reference-pose requirement retains geometry and performs no build");
            ActionRegistry.RegisterRuntime(new(){Id="preparation-t"},sourceT,folder,"fixture");Tick();
            check(sourceT.IsPrepared(actor.avatar)&&ReferenceEquals(originalSnapshot,Snapshot())&&MmdRigCache.BuildCount==bothBuilds,
                "Readding a cached reference pose reuses its rig and the existing actor snapshot");

            actor.avatar.transform.localScale=Vector3.one*1.2f;
            MmdRigCache.Invalidate();
            Await(()=>sourceA.IsPrepared(actor.avatar)&&sourceT.IsPrepared(actor.avatar),"changed root metric");
            var scaledSnapshot=(MmdTargetSnapshot)Snapshot();
            check(!ReferenceEquals(originalSnapshot,scaledSnapshot)&&Math.Abs(scaledSnapshot.MetricX.magnitude-1.2f)<1e-5,
                "A real scale change invalidates the actor snapshot and captures the new world metric");
            check(MmdRigCache.BuildCount==bothBuilds+2,"A real new shape rebuilds both requested reference poses");

            var oldMesh=actor.renderer.sharedMesh;
            actor.renderer.sharedMesh=new Mesh{ShapeNames=oldMesh.ShapeNames.ToArray(),bindposes=oldMesh.bindposes.ToArray()};
            check(!sourceA.IsPrepared(actor.avatar),"Replacing the body mesh still invalidates stale renderer ownership");
            int beforeMeshRebind=MmdRigCache.BuildCount;
            Await(()=>sourceA.IsPrepared(actor.avatar)&&sourceT.IsPrepared(actor.avatar),"replacement mesh");
            object meshSnapshot=Snapshot();
            check(!ReferenceEquals(scaledSnapshot,meshSnapshot)&&MmdRigCache.BuildCount==beforeMeshRebind,
                "A replacement mesh is rebound using fresh references while identical geometry reuses both solvers");

            var replacement=createAvatar();actors.Add(replacement.avatar);
            UnityEngine.Object.Destroy(actor.avatar);
            ActionTargetApi.Target=replacement.avatar;
            Await(()=>sourceA.IsPrepared(replacement.avatar)&&sourceT.IsPrepared(replacement.avatar),"replacement actor");
            check(!ReferenceEquals(meshSnapshot,Snapshot()),"Destroying and replacing the target captures the replacement actor");
            var rebound=MmdRigCache.Rent(replacement.avatar,MmdReferencePose.A);
            try
            {
                check(rebound.target==replacement.avatar&&rebound.bindings.All(b=>b.live==replacement.bones[b.path]),
                    "Reused helpers bind every writable bone to the replacement actor rather than destroyed references");
            }
            finally{MmdRigCache.Return(rebound);}
        }
        finally
        {
            pump?.OnDestroy();MmdRigCache.Clear();
            foreach(var actor in actors)UnityEngine.Object.Destroy(actor);
            ActionRegistry.Registered=previousActions;ActionRegistry.Revision=previousRevision;
            ActionTargetApi.Target=previousTarget;ActionTargetApi.IsReady=previousReady;
            Directory.Delete(folder,true);
        }
    }

    static void WriteMotion(string path)
    {
        using var writer=new BinaryWriter(File.Create(path));
        void Text(string text,int size)
        {
            var bytes=Encoding.GetEncoding(932).GetBytes(text);
            writer.Write(bytes);writer.Write(new byte[size-bytes.Length]);
        }
        Text("Vocaloid Motion Data 0002",30);Text("preparation-fixture",20);writer.Write(2u);
        foreach(uint frame in new[]{0u,30u})
        {
            Text("上半身",15);writer.Write(frame);
            writer.Write(0f);writer.Write(0f);writer.Write(0f);
            writer.Write(0f);writer.Write(0f);writer.Write(0f);writer.Write(1f);
            writer.Write(new byte[64]);
        }
        writer.Write(0u);
    }
}
