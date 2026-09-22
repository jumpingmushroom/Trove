using System.Collections.Generic;
using Trove.Core;
using Trove.Model;
using UnityEngine;

namespace Trove.UI
{
    /// <summary>
    /// One vanilla pin per patch, carrying the item's own sprite, the count as name, and the
    /// vanilla cross while nothing in the patch is ready.
    ///
    /// Pins are added with save:false, so they are never written to the map file, never shared
    /// via the cartography table, and — because Minimap.GetClosestPin only considers saved
    /// pins — cannot be clicked, checked or right-click-deleted by the game. Type is PinType.None
    /// so no icon filter is flipped; visibility is this mod's own setting.
    /// </summary>
    internal sealed class ResourcePins
    {
        private static ResourcePins s_current;

        private readonly Dictionary<int, Minimap.PinData> _pins = new Dictionary<int, Minimap.PinData>();
        private readonly List<int> _gone = new List<int>();
        private Minimap _map;
        private int _version = -1;
        private double _nextReadyCheck;
        private bool _shown;
        private bool _forceRebuild;

        public ResourcePins()
        {
            s_current = this;
        }

        public static void InvalidateAll()
        {
            if (s_current != null)
                s_current.Invalidate();
        }

        public void Invalidate()
        {
            _forceRebuild = true;
        }

        public void Clear()
        {
            _pins.Clear();
            _map = null;
            _version = -1;
            _shown = false;
        }

        public void Sync()
        {
            Minimap map = Minimap.instance;
            if (map == null)
            {
                Clear();
                return;
            }

            if (!ReferenceEquals(map, _map))
            {
                Clear();
                _map = map;
            }

            bool show = PluginConfig.PinsEnabled.Value && PatchCache.Loaded;
            if (!show)
            {
                if (_shown)
                    RemoveAll(map);
                _shown = false;
                return;
            }

            // Members ripen on their own clock, so the checked state needs a periodic look
            // even when nothing was picked.
            double now = PatchStore.Now();
            bool ripen = now >= _nextReadyCheck;
            if (ripen)
                _nextReadyCheck = now + 30.0;

            bool changed = !_shown || _forceRebuild || PatchStore.Version != _version || ripen;
            _shown = true;
            if (!changed)
                return;

            if (_forceRebuild)
            {
                RemoveAll(map);
                _forceRebuild = false;
            }

            _version = PatchStore.Version;
            bool counts = PluginConfig.ShowCounts.Value;
            bool hideChecked = PluginConfig.HideChecked.Value;

            var seen = new HashSet<int>();
            IReadOnlyList<Patch> patches = PatchStore.Patches;
            for (int i = 0; i < patches.Count; i++)
            {
                Patch p = patches[i];
                if (p.Count == 0)
                    continue;
                int ready = p.ReadyCount(now);
                bool isChecked = p.ManualChecked || (p.Kind != ResourceKind.Ore && ready == 0);
                if (isChecked && hideChecked)
                    continue;

                ResourceCatalog.Info info = ResourceCatalog.ForItem(p.Item);
                string name = ResourceCatalog.DisplayName(info);
                if (string.IsNullOrEmpty(name))
                    name = p.Item;
                if (counts && p.Count > 1)
                    name += " ×" + p.Count;

                seen.Add(p.Id);
                Minimap.PinData pin;
                if (_pins.TryGetValue(p.Id, out pin))
                {
                    if (pin.m_name != name)
                    {
                        // A name change needs a new PinNameData; simplest is a fresh pin.
                        map.RemovePin(pin);
                        pin = null;
                    }
                    else
                    {
                        pin.m_pos = p.Center;
                        pin.m_checked = isChecked;
                    }
                }

                if (pin == null)
                {
                    pin = CreatePin(map, p.Center, name, info != null ? info.Icon : null, isChecked);
                    _pins[p.Id] = pin;
                }
            }

            _gone.Clear();
            foreach (KeyValuePair<int, Minimap.PinData> kv in _pins)
                if (!seen.Contains(kv.Key))
                    _gone.Add(kv.Key);

            for (int i = 0; i < _gone.Count; i++)
            {
                map.RemovePin(_pins[_gone[i]]);
                _pins.Remove(_gone[i]);
            }

            map.m_pinUpdateRequired = true;
        }

        /// <summary>
        /// What Minimap.AddPin does, minus the icon-filter flip and the optional PlatformUserID
        /// author parameter (whose type lives in an assembly this project does not reference).
        /// The sprite is copied into the marker when it is created, so it is set here, before
        /// the UpdatePins pass this schedules.
        /// </summary>
        private static Minimap.PinData CreatePin(Minimap map, Vector3 pos, string name, Sprite icon, bool isChecked)
        {
            if (icon == null)
                icon = map.GetSprite(Minimap.PinType.Icon3);
            var pin = new Minimap.PinData
            {
                m_type = Minimap.PinType.None,
                m_name = name ?? "",
                m_pos = pos,
                m_icon = icon,
                m_save = false,
                m_checked = isChecked,
                m_ownerID = 0L,
            };
            if (pin.m_name.Length > 0)
                pin.m_NamePinData = new Minimap.PinNameData(pin);
            map.m_pins.Add(pin);
            map.m_pinUpdateRequired = true;
            return pin;
        }

        private void RemoveAll(Minimap map)
        {
            foreach (KeyValuePair<int, Minimap.PinData> kv in _pins)
                map.RemovePin(kv.Value);
            _pins.Clear();
            _version = -1;
        }
    }
}
