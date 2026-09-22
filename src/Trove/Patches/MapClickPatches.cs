using Trove.Core;
using Trove.Model;
using Patch = Trove.Model.Patch;
using Trove.UI;
using HarmonyLib;
using UnityEngine;

namespace Trove.Patches
{
    /// <summary>
    /// The game only clicks saved pins (GetClosestPin filters on m_save), so ours need their
    /// own handling. A vanilla pin under the cursor wins: the game keeps its behaviour and ours
    /// stays out of the way.
    /// </summary>
    internal static class MapClick
    {
        public static Patch Target(Minimap map)
        {
            if (!PluginConfig.ClickEnabled.Value || !PluginConfig.PinsEnabled.Value)
                return null;
            Patch p = PatchHover.Current;
            if (p == null)
                return null;
            Vector3 pos = map.ScreenToWorldPoint(ZInput.pointerPosition);
            float radius = map.PinInteractRadius;
            if (map.GetClosestPin(pos, radius) != null)
                return null;
            if (Utils.DistanceXZ(p.Center, pos) > radius)
                return null;
            return p;
        }
    }

    [HarmonyPatch(typeof(Minimap), nameof(Minimap.OnMapLeftClick))]
    public static class Minimap_OnMapLeftClick_Patch
    {
        private static bool Prefix(Minimap __instance)
        {
            Patch p = MapClick.Target(__instance);
            if (p == null)
                return true;
            PatchStore.ToggleManualCheck(p);
            __instance.m_pinUpdateRequired = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(Minimap), "RemovePinUnderPointer")]
    public static class Minimap_RemovePinUnderPointer_Patch
    {
        private static bool Prefix(Minimap __instance)
        {
            Patch p = MapClick.Target(__instance);
            if (p == null)
                return true;
            if (PluginConfig.Verbose.Value)
                TrovePlugin.Log.LogDebug(string.Format("forgot patch #{0} {1} x{2}", p.Id, p.Item, p.Count));
            PatchStore.Forget(p);
            __instance.m_pinUpdateRequired = true;
            return false;
        }
    }

    /// <summary>A double-click on one of our pins must not open the vanilla name-a-pin box.</summary>
    [HarmonyPatch(typeof(Minimap), nameof(Minimap.OnMapDblClick))]
    public static class Minimap_OnMapDblClick_Patch
    {
        private static bool Prefix(Minimap __instance)
        {
            return MapClick.Target(__instance) == null;
        }
    }
}
