using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.AI;
using ExposureUnnoticed2.Object3D.NPC.Script;
using ExposureUnnoticed2.Object3D.Player.Scripts;
using ExposureUnnoticed2.Scripts.InGame;
using ExposureUnnoticed2.Object3D.IngameManager;
using ExposureUnnoticed2.Scripts.Base;

namespace SFM.NpcSpectators;

internal sealed class Spectator
{
    internal readonly NpcController Npc;
    internal readonly int Slot, Id;
    internal readonly Vector3 Destination;
    internal bool Arrived { get; private set; }
    readonly NavMeshAgent agent;
    readonly NpcLocomotion locomotion;
    readonly bool stoppedBefore, pathBefore;
    readonly Vector3 destinationBefore, cachedDestinationBefore;
    readonly Quaternion rotationTargetBefore;
    readonly float speedBefore;

    internal Spectator(NpcController npc, int slot, Vector3 destination)
    {
        Npc = npc; Slot = slot; Destination = destination; Id = npc.id;
        agent = npc.Nca.NavAgent; locomotion = npc.Nca.NpcLocomotion;
        stoppedBefore = agent.isStopped;
        pathBefore = agent.hasPath; destinationBefore = agent.destination;
        cachedDestinationBefore = locomotion.lastSetDestinationPoint; rotationTargetBefore = locomotion.targetRotation;
        speedBefore = npc.Nca.NpcAnimationManager.speed.Target;
    }
    internal void Acquire()
    {
        // Keep the native agent flags and movement owner. Gathering supplies
        // a temporary destination, just like native interaction approaches.
        locomotion.SetMoveZero(); agent.isStopped = false;
        if (!agent.SetDestination(Destination)) throw new InvalidOperationException("Crowd destination was rejected.");
        locomotion.lastSetDestinationPoint = Destination;
    }
    internal void Drive(Vector3 center)
    {
        var root = Npc.Transform;
        var animation = Npc.Nca.NpcAnimationManager;
        Arrived = CrowdDriver.HorizontalSquare(root.position - Destination) <= 0.35f * 0.35f;
        if (!Arrived)
        {
            agent.isStopped = false; animation.SetSpeed(1);
            // Existing native approach routine owns turning, moveV, speed and
            // agent synchronization; native FixedUpdate/collision stays intact.
            locomotion.SimpleUpdateFromOtherState(Destination, 0.35f);
            return;
        }
        locomotion.SetMoveZero(); animation.SetSpeed(0); agent.isStopped = true;
        Vector3 facing = center - root.position; facing.y = 0;
        if (facing.sqrMagnitude > 0.001f) locomotion.targetRotation = Quaternion.LookRotation(facing);
        root.rotation = Quaternion.RotateTowards(root.rotation, locomotion.targetRotation, Time.deltaTime * 90);
    }
    internal void Restore()
    {
        if (Npc.GameObject == null || Npc.Transform == null) return;
        // A changed native state owns its own movement settings. Never restore the
        // patrol snapshot over a police/discovery/interaction transition.
        if (Npc.Nca.StateModel.IsDestroy || Npc.Nca.StateModel.CurrentState != NpcStateModel.State.Idle) return;
        locomotion.SetMoveZero(); locomotion.lastSetDestinationPoint = cachedDestinationBefore;
        locomotion.targetRotation = rotationTargetBefore;
        Npc.Nca.NpcAnimationManager.SetSpeed(speedBefore);
        if (agent == null || !agent.enabled || !agent.isOnNavMesh) return;
        agent.nextPosition = Npc.Transform.position;
        if (pathBefore && float.IsFinite(destinationBefore.x) && float.IsFinite(destinationBefore.y) && float.IsFinite(destinationBefore.z)) agent.SetDestination(destinationBefore);
        else agent.ResetPath();
        agent.isStopped = stoppedBefore;
    }


}
