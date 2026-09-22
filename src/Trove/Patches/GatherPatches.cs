using Trove.Core;
using Trove.Model;
using Patch = Trove.Model.Patch;
using HarmonyLib;
using UnityEngine;

namespace Trove.Patches
{
    /// <summary>
    /// Pickable.Interact runs only on the picking client (Player.Update is owner-only), so a
    /// prefix here is exactly "I picked this", once per press. The guard mirrors the game's
    /// own: not already picked, enabled, and a player did it.
    /// </summary>
    [HarmonyPatch(typeof(Pickable), nameof(Pickable.Interact))]
    public static class Pickable_Interact_Patch
    {
        private static void Prefix(Pickable __instance, Humanoid character)
        {
            if (!PluginConfig.GatherEnabled.Value)
                return;
            if (__instance == null || character == null || !ReferenceEquals(character, Player.m_localPlayer))
                return;
            if (__instance.GetPicked() || __instance.GetEnabled == 0)
                return;
            ZNetView nview = __instance.m_nview;
            if (nview == null || !nview.IsValid())
                return;

            Capture.Pick(__instance);
        }
    }

    /// <summary>
    /// Runs on every client for every loaded pickable, whoever picked it, and again when it
    /// grows back. Free corrections for members we track; nothing is added here.
    /// </summary>
    [HarmonyPatch(typeof(Pickable), nameof(Pickable.SetPicked))]
    public static class Pickable_SetPicked_Patch
    {
        private static void Postfix(Pickable __instance, bool picked)
        {
            if (__instance == null || !PatchCache.Loaded)
                return;
            Capture.ObservePickable(__instance, picked);
        }
    }

    /// <summary>A pickable coming into range: sync the member with what its ZDO says.</summary>
    [HarmonyPatch(typeof(Pickable), "Awake")]
    public static class Pickable_Awake_Patch
    {
        private static void Postfix(Pickable __instance)
        {
            if (__instance == null || !PatchCache.Loaded)
                return;
            ZNetView nview = __instance.m_nview;
            if (nview == null || !nview.IsValid())
                return;
            Capture.ObservePickable(__instance, __instance.GetPicked());
        }
    }

    internal static class Capture
    {
        public static void Pick(Pickable pickable)
        {
            ResourceCatalog.EnsureBuilt();
            PatchCache.EnsureLoaded();
            if (!PatchCache.Loaded)
                return;

            string prefab = Utils.GetPrefabName(pickable.gameObject);
            ResourceCatalog.Info info = ResourceCatalog.Get(prefab);
            if (info == null)
                return;
            if (info.Kind == ResourceKind.OneShot && !PluginConfig.PinOneShot.Value)
                return;
            if (info.Kind != ResourceKind.Pickable && info.Kind != ResourceKind.OneShot)
                return;

            Vector3 pos = pickable.transform.position;
            if (ResourceCatalog.Split(PluginConfig.ExcludedItems.Value).Contains(info.Item))
                return;
            if (PluginConfig.SkipPlayerBases.Value && EffectArea.IsPointInsideArea(pos, EffectArea.Type.PlayerBase) != null)
            {
                if (PluginConfig.Verbose.Value)
                    TrovePlugin.Log.LogDebug("skipped " + info.Item + " inside a player base");
                return;
            }

            bool created;
            Patch patch = PatchStore.Record(info.Kind, info.Item, prefab, pos, pickable.m_respawnTimeMinutes,
                picked: true, PatchStore.Now(), PluginConfig.GatherClusterRadius.Value, out created);
            if (patch == null)
                return;

            if (PluginConfig.Verbose.Value)
                TrovePlugin.Log.LogDebug(string.Format("pick {0} at ({1:0},{2:0}) -> patch #{3} x{4}{5}",
                    info.Item, pos.x, pos.z, patch.Id, patch.Count, created ? " (new)" : ""));

            if (created)
                Notify.NewPatch(info);
        }

        public static void ObservePickable(Pickable pickable, bool picked)
        {
            Vector3 pos = pickable.transform.position;
            double pickedSec = 0.0;
            if (picked)
            {
                ZNetView nview = pickable.m_nview;
                if (nview != null && nview.IsValid())
                {
                    long ticks = nview.GetZDO().GetLong(ZDOVars.s_pickedTime, 0L);
                    if (ticks > 0)
                        pickedSec = ticks / 10000000.0;
                }
            }
            PatchStore.Observe(pos, picked, pickedSec);
        }
    }

    internal static class Notify
    {
        public static void NewPatch(ResourceCatalog.Info info)
        {
            if (!PluginConfig.Toast.Value)
                return;
            MessageHud hud = MessageHud.instance;
            if (hud == null)
                return;
            hud.ShowMessage(MessageHud.MessageType.TopLeft, "Remembered: " + ResourceCatalog.DisplayName(info), 0, info.Icon);
        }
    }
}
