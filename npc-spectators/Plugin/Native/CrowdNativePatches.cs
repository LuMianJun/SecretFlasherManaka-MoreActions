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

[HarmonyPatch(typeof(NpcLocomotion), "OnUpdate")]
static class CrowdLocomotionPatch
{
    static bool Prefix(NpcLocomotion __instance) => !(CrowdDriver.Current?.Drive(__instance) ?? false);
}

[HarmonyPatch(typeof(NpcController), "ChangeState")]
static class CrowdInterruptPatch
{
    static void Prefix(NpcController __instance, NpcStateModel.State __0)
    {
        // Release BEFORE original OnEnter writes the next state's movement settings.
        if (__0 != NpcStateModel.State.Idle) CrowdDriver.Current?.Release(__instance.Pointer, "native state transition " + __0);
    }
}

[HarmonyPatch(typeof(NpcManager), "OnUpdate")]
static class CrowdTickPatch
{
    static void Prefix() { CrowdDriver.Current?.Update(); }
}
