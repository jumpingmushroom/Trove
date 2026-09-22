using Trove.Core;
using Trove.Model;
using Patch = Trove.Model.Patch;
using HarmonyLib;
using UnityEngine;

namespace Trove.Patches
{
    /// <summary>
    /// IDestructible.Damage runs on the attacker's client before the owner RPC, so it is the one
    /// place a hit on a node owned by someone else is still visible to us. Tool tier is only
    /// checked in the owner RPC, so it is repeated here: a club bouncing off silver is no find.
    /// </summary>
    [HarmonyPatch(typeof(MineRock5), nameof(MineRock5.Damage))]
    public static class MineRock5_Damage_Patch
    {
        private static void Prefix(MineRock5 __instance, HitData hit)
        {
            MineCapture.Hit(__instance.gameObject, hit, __instance.m_minToolTier);
        }
    }

    [HarmonyPatch(typeof(MineRock), nameof(MineRock.Damage))]
    public static class MineRock_Damage_Patch
    {
        private static void Prefix(MineRock __instance, HitData hit)
        {
            MineCapture.Hit(__instance.gameObject, hit, __instance.m_minToolTier);
        }
    }

    [HarmonyPatch(typeof(Destructible), nameof(Destructible.Damage))]
    public static class Destructible_Damage_Patch
    {
        private static void Prefix(Destructible __instance, HitData hit)
        {
            MineCapture.Hit(__instance.gameObject, hit, __instance.m_minToolTier);
        }
    }

    /// <summary>
    /// ZNetScene.Destroy is the "gone for good" path (the ZDO is destroyed); unloading an area
    /// goes through RemoveObjects and Object.Destroy instead. A copper shell being replaced by
    /// its MineRock5 also comes through here and must not count: the frac carries on.
    /// </summary>
    [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.Destroy))]
    public static class ZNetScene_Destroy_Patch
    {
        private static void Prefix(GameObject go)
        {
            if (go == null || !PatchCache.Loaded)
                return;
            MineCapture.Destroyed(go);
        }
    }

    /// <summary>
    /// Piece health arrives on every client: from the ZDO at Awake and on data-revision change
    /// (LoadHealth), and per destroyed piece from the owner (RPC_SetAreaHealth). Copper and
    /// silver keep pieces below the dig limit, so "mined out" is a share, not all-destroyed.
    /// </summary>
    [HarmonyPatch(typeof(MineRock5), "LoadHealth")]
    public static class MineRock5_LoadHealth_Patch
    {
        private static void Postfix(MineRock5 __instance)
        {
            MineCapture.CheckDepleted(__instance);
        }
    }

    [HarmonyPatch(typeof(MineRock5), "RPC_SetAreaHealth")]
    public static class MineRock5_RPC_SetAreaHealth_Patch
    {
        private static void Postfix(MineRock5 __instance)
        {
            MineCapture.CheckDepleted(__instance);
        }
    }

    internal static class MineCapture
    {
        public static void CheckDepleted(MineRock5 rock)
        {
            if (rock == null || !PatchCache.Loaded || rock.m_hitAreas == null || rock.m_hitAreas.Count == 0)
                return;
            Vector3 pos = rock.transform.position;
            if (PatchStore.Find(pos) == null)
                return;
            int dead = 0;
            for (int i = 0; i < rock.m_hitAreas.Count; i++)
                if (rock.m_hitAreas[i].m_health <= 0f)
                    dead++;
            int percent = dead * 100 / rock.m_hitAreas.Count;
            if (percent < PluginConfig.DepletedPercent.Value)
                return;
            if (PatchStore.NoteDestroyed(pos) && PluginConfig.Verbose.Value)
                TrovePlugin.Log.LogDebug(string.Format("mined out ({0}%): {1} at ({2:0},{3:0})",
                    percent, Utils.GetPrefabName(rock.gameObject), pos.x, pos.z));
        }

        public static void Hit(GameObject go, HitData hit, int minToolTier)
        {
            if (!PluginConfig.MineEnabled.Value || hit == null || go == null)
                return;
            Player local = Player.m_localPlayer;
            if (local == null || hit.m_attacker != local.GetZDOID())
                return;
            if (hit.m_skill != Skills.SkillType.Pickaxes)
                return;
            if (!hit.CheckToolTier(minToolTier))
                return;

            ResourceCatalog.EnsureBuilt();
            PatchCache.EnsureLoaded();
            if (!PatchCache.Loaded)
                return;

            string prefab = Utils.GetPrefabName(go);
            ResourceCatalog.Info info = ResourceCatalog.Get(prefab);
            if (info == null || info.Kind != ResourceKind.Ore)
                return;

            Vector3 pos = go.transform.position;
            bool created;
            Patch patch = PatchStore.Record(ResourceKind.Ore, info.Item, prefab, pos, 0f,
                picked: false, PatchStore.Now(), PluginConfig.MineClusterRadius.Value, out created);
            if (patch == null)
                return;

            if (PluginConfig.Verbose.Value)
                TrovePlugin.Log.LogDebug(string.Format("hit {0} ({1}) at ({2:0},{3:0}) -> patch #{4} x{5}{6}",
                    info.Item, prefab, pos.x, pos.z, patch.Id, patch.Count, created ? " (new)" : ""));

            if (created)
                Notify.NewPatch(info);
        }

        public static void Destroyed(GameObject go)
        {
            Vector3 pos = go.transform.position;
            if (PatchStore.Find(pos) == null)
                return;

            Destructible d = go.GetComponent<Destructible>();
            if (d != null && d.m_spawnWhenDestroyed != null && d.m_spawnWhenDestroyed.GetComponent<MineRock5>() != null)
                return; // the shell becomes the deposit; same spot, same entry

            if (go.GetComponent<MineRock5>() == null && go.GetComponent<MineRock>() == null
                && d == null && go.GetComponent<Pickable>() == null)
                return;

            if (PatchStore.NoteDestroyed(pos) && PluginConfig.Verbose.Value)
                TrovePlugin.Log.LogDebug(string.Format("gone: {0} at ({1:0},{2:0})", Utils.GetPrefabName(go), pos.x, pos.z));
        }
    }
}
