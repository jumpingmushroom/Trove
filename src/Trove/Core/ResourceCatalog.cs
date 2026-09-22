using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Trove.Model;
using UnityEngine;

namespace Trove.Core
{
    /// <summary>
    /// What each prefab is to this mod, read from the components once per world. Nothing is
    /// hard-coded: a Pickable with a respawn time is a patch plant, a rock whose drop table holds
    /// anything but stone is an ore node. Modded and Deep North content classifies itself.
    /// </summary>
    public static class ResourceCatalog
    {
        public sealed class Info
        {
            public string Prefab = "";
            public ResourceKind Kind;
            public string Item = "";
            public string ItemToken = "";
            public float RespawnMinutes;
            public Sprite Icon;
            public string Via = "";

            /// <summary>Every drop the node can yield, for "why is it called that" diagnostics.</summary>
            public string Drops = "";
        }

        private static readonly Dictionary<string, Info> _byPrefab = new Dictionary<string, Info>();
        private static readonly Dictionary<string, Info> _byItem = new Dictionary<string, Info>();
        private static HashSet<string> _ignoredDrops = new HashSet<string>();
        private static string[] _excludedPrefabs = new string[0];
        private static bool _built;

        public static bool Built => _built;
        public static IEnumerable<Info> All => _byPrefab.Values;

        public static void Clear()
        {
            _byPrefab.Clear();
            _byItem.Clear();
            _built = false;
        }

        public static Info Get(string prefab)
        {
            Info i;
            return prefab != null && _byPrefab.TryGetValue(prefab, out i) ? i : null;
        }

        /// <summary>Icon and display token for an item prefab name, via any catalogued node that yields it.</summary>
        public static Info ForItem(string item)
        {
            Info i;
            return item != null && _byItem.TryGetValue(item, out i) ? i : null;
        }

        public static void EnsureBuilt()
        {
            if (_built)
                return;
            ZNetScene scene = ZNetScene.instance;
            if (scene == null || scene.m_prefabs == null || scene.m_prefabs.Count == 0)
                return;

            _ignoredDrops = Split(PluginConfig.IgnoredDrops.Value);
            var patterns = new List<string>(Split(PluginConfig.ExcludedPrefabs.Value));
            _excludedPrefabs = patterns.ToArray();
            int plants = 0, seeds = 0, ores = 0;

            foreach (GameObject prefab in scene.m_prefabs)
            {
                if (prefab == null)
                    continue;
                Info info = Classify(prefab);
                if (info == null)
                    continue;
                _byPrefab[info.Prefab] = info;
                if (!_byItem.ContainsKey(info.Item))
                    _byItem[info.Item] = info;
                switch (info.Kind)
                {
                    case ResourceKind.Pickable: plants++; break;
                    case ResourceKind.OneShot: seeds++; break;
                    case ResourceKind.Ore: ores++; break;
                }
            }

            _built = true;
            TrovePlugin.Log.LogInfo(string.Format(
                "catalogue: {0} respawning pickable(s), {1} one-shot pickable(s), {2} ore node prefab(s); 'trove catalog' lists them",
                plants, seeds, ores));
        }

        private static Info Classify(GameObject prefab)
        {
            Pickable pickable = prefab.GetComponent<Pickable>();
            if (pickable != null)
            {
                if (pickable.m_itemPrefab == null)
                    return null;
                var info = new Info
                {
                    Prefab = prefab.name,
                    Kind = pickable.m_respawnTimeMinutes > 0f ? ResourceKind.Pickable : ResourceKind.OneShot,
                    Item = pickable.m_itemPrefab.name,
                    RespawnMinutes = pickable.m_respawnTimeMinutes,
                    Via = "Pickable",
                };
                FillItem(info, pickable.m_itemPrefab);
                return info;
            }

            DropTable table = null;
            string via = null;

            MineRock5 rock5 = prefab.GetComponent<MineRock5>();
            if (rock5 != null)
            {
                table = rock5.m_dropItems;
                via = "MineRock5";
            }
            else
            {
                MineRock rock = prefab.GetComponent<MineRock>();
                if (rock != null)
                {
                    table = rock.m_dropItems;
                    via = "MineRock";
                }
                else
                {
                    Destructible d = prefab.GetComponent<Destructible>();
                    if (d != null)
                    {
                        // Copper and silver: a shell that becomes a MineRock5 on the first hit.
                        MineRock5 inner = d.m_spawnWhenDestroyed != null ? d.m_spawnWhenDestroyed.GetComponent<MineRock5>() : null;
                        if (inner != null)
                        {
                            table = inner.m_dropItems;
                            via = "Destructible>" + d.m_spawnWhenDestroyed.name;
                        }
                        else
                        {
                            // A plain destructible is a node only if it cannot be chopped or
                            // smashed and yields to a pickaxe: tin, obsidian, ice. Stumps,
                            // bushes, barrels and furniture drop things too but fall to an axe.
                            DropOnDestroyed drop = prefab.GetComponent<DropOnDestroyed>();
                            if (drop != null && IsPickaxeOnly(d.m_damages))
                            {
                                table = drop.m_dropWhenDestroyed;
                                via = "Destructible+DropOnDestroyed";
                            }
                        }
                    }
                }
            }

            if (table == null || table.m_drops == null)
                return null;

            // Name the node after its bulk yield: the heaviest-weighted drop that is worth
            // keeping. Drop-table order is arbitrary, and the rarest row is the wrong answer —
            // a mud pile is an iron source that happens to cough up a withered bone at 1%.
            GameObject ore = null;
            float bestWeight = float.MinValue;
            var drops = new StringBuilder();
            for (int i = 0; i < table.m_drops.Count; i++)
            {
                GameObject item = table.m_drops[i].m_item;
                if (item == null || item.GetComponent<ItemDrop>() == null)
                    continue;
                float weight = table.m_drops[i].m_weight;
                if (drops.Length > 0)
                    drops.Append(", ");
                drops.Append(item.name).Append(' ').Append(weight.ToString("0.##", CultureInfo.InvariantCulture));
                if (_ignoredDrops.Contains(item.name))
                {
                    drops.Append("(ignored)");
                    continue;
                }
                if (ore == null || weight > bestWeight)
                {
                    ore = item;
                    bestWeight = weight;
                }
            }
            if (ore == null)
                return null;
            if (IsExcludedPrefab(prefab.name))
                return null;

            var oreInfo = new Info
            {
                Prefab = prefab.name,
                Kind = ResourceKind.Ore,
                Item = ore.name,
                Via = via,
                Drops = drops.ToString(),
            };
            FillItem(oreInfo, ore);
            return oreInfo;
        }

        /// <summary>Exact name, or a trailing * for a prefix match.</summary>
        private static bool IsExcludedPrefab(string name)
        {
            for (int i = 0; i < _excludedPrefabs.Length; i++)
            {
                string p = _excludedPrefabs[i];
                if (p.Length == 0)
                    continue;
                if (p[p.Length - 1] == '*')
                {
                    if (name.StartsWith(p.Substring(0, p.Length - 1), StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                else if (string.Equals(name, p, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static bool IsPickaxeOnly(HitData.DamageModifiers m)
        {
            return m.m_pickaxe != HitData.DamageModifier.Immune
                && m.m_chop == HitData.DamageModifier.Immune
                && m.m_blunt == HitData.DamageModifier.Immune
                && m.m_slash == HitData.DamageModifier.Immune;
        }

        private static void FillItem(Info info, GameObject itemPrefab)
        {
            ItemDrop drop = itemPrefab.GetComponent<ItemDrop>();
            if (drop == null || drop.m_itemData == null || drop.m_itemData.m_shared == null)
            {
                info.ItemToken = info.Item;
                return;
            }
            info.ItemToken = drop.m_itemData.m_shared.m_name ?? info.Item;
            try
            {
                Sprite[] icons = drop.m_itemData.m_shared.m_icons;
                if (icons != null && icons.Length > 0)
                    info.Icon = drop.m_itemData.GetIcon();
            }
            catch (Exception ex)
            {
                if (PluginConfig.Verbose.Value)
                    TrovePlugin.Log.LogDebug("no icon for " + info.Item + ": " + ex.Message);
            }
        }

        public static HashSet<string> Split(string csv)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(csv))
                return set;
            foreach (string part in csv.Split(','))
            {
                string s = part.Trim();
                if (s.Length > 0)
                    set.Add(s);
            }
            return set;
        }

        /// <summary>Display name for an item token, falling back to the prefab name.</summary>
        public static string DisplayName(Info info)
        {
            if (info == null)
                return "";
            string token = info.ItemToken;
            if (string.IsNullOrEmpty(token))
                return info.Item;
            Localization loc = Localization.instance;
            string s = loc != null ? loc.Localize(token) : token;
            if (string.IsNullOrEmpty(s) || s.StartsWith("[", StringComparison.Ordinal))
                return info.Item;
            return s;
        }
    }
}
