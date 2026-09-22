using System;
using System.Collections.Generic;
using Trove.Model;
using UnityEngine;

namespace Trove.Core
{
    /// <summary>
    /// Everything the mod remembers for the current world: patches of members keyed by rounded
    /// position. Picks and hits come in from the patches, corrections from SetPicked and
    /// ZNetScene.Destroy, and the pins read the result. Persistence is PatchCache's job.
    /// </summary>
    public static class PatchStore
    {
        private static readonly List<Patch> _patches = new List<Patch>();
        private static readonly Dictionary<string, Member> _members = new Dictionary<string, Member>();
        private static readonly Dictionary<Member, Patch> _owner = new Dictionary<Member, Patch>();
        private static readonly HashSet<string> _forgotten = new HashSet<string>();
        private static int _nextId = 1;

        /// <summary>Bumped on every change the pins should react to.</summary>
        public static int Version { get; private set; }

        public static bool Dirty { get; private set; }

        public static IReadOnlyList<Patch> Patches => _patches;
        public static int MemberCount => _members.Count;
        public static IEnumerable<string> Forgotten => _forgotten;
        public static int ForgottenCount => _forgotten.Count;

        public static void Clear()
        {
            _patches.Clear();
            _members.Clear();
            _owner.Clear();
            _forgotten.Clear();
            _nextId = 1;
            Dirty = false;
            Version++;
        }

        public static void RestoreForgotten(string key)
        {
            _forgotten.Add(key);
        }

        public static void Unforget()
        {
            if (_forgotten.Count == 0)
                return;
            _forgotten.Clear();
            Touch();
        }

        /// <summary>Wipe everything and make sure the empty state is written.</summary>
        public static void ForgetAll()
        {
            Clear();
            Dirty = true;
        }

        public static void MarkSaved()
        {
            Dirty = false;
        }

        public static double Now()
        {
            ZNet net = ZNet.instance;
            return net != null ? net.GetTimeSeconds() : 0.0;
        }

        public static Member Find(Vector3 pos)
        {
            Member m;
            return _members.TryGetValue(Member.MakeKey(pos), out m) ? m : null;
        }

        public static Patch PatchOf(Member m)
        {
            Patch p;
            return _owner.TryGetValue(m, out p) ? p : null;
        }

        /// <summary>
        /// A pick or hit by the local player. Returns the patch, and whether it was created by
        /// this call (for the toast).
        /// </summary>
        public static Patch Record(ResourceKind kind, string item, string prefab, Vector3 pos, float respawnMinutes,
            bool picked, double now, float clusterRadius, out bool created)
        {
            created = false;
            string key = Member.MakeKey(pos);
            if (_forgotten.Contains(key))
                return null;
            Member m;
            Patch patch;
            if (_members.TryGetValue(key, out m))
            {
                patch = PatchOf(m);
                if (patch != null && patch.Item == item)
                {
                    bool changed = false;
                    if (picked && !m.Picked)
                    {
                        m.Picked = true;
                        m.PickedSec = now;
                        changed = true;
                    }
                    if (m.RespawnMinutes != respawnMinutes)
                    {
                        m.RespawnMinutes = respawnMinutes;
                        changed = true;
                    }
                    if (changed)
                        Touch();
                    return patch;
                }
                // Same spot, different item (the prefab changed under us): start over.
                RemoveMember(m);
            }

            patch = Nearest(kind, item, pos, clusterRadius);
            if (patch == null)
            {
                patch = new Patch { Id = _nextId++, Kind = kind, Item = item, Center = pos };
                _patches.Add(patch);
                created = true;
            }

            m = new Member
            {
                Key = key,
                Pos = pos,
                Prefab = prefab,
                RespawnMinutes = respawnMinutes,
                Picked = picked,
                PickedSec = picked ? now : 0.0,
            };
            _members[key] = m;
            _owner[m] = patch;
            patch.Members.Add(m);
            patch.Recenter();
            Touch();
            return patch;
        }

        /// <summary>Used by the cache loader: no clustering, patches are restored as saved.</summary>
        public static void Restore(int patchId, ResourceKind kind, string item, string prefab, Vector3 pos,
            float respawnMinutes, bool picked, double pickedSec, bool manualChecked)
        {
            Patch patch = null;
            for (int i = 0; i < _patches.Count; i++)
                if (_patches[i].Id == patchId)
                {
                    patch = _patches[i];
                    break;
                }
            if (patch == null)
            {
                patch = new Patch { Id = patchId, Kind = kind, Item = item, Center = pos };
                _patches.Add(patch);
                if (patchId >= _nextId)
                    _nextId = patchId + 1;
            }
            if (manualChecked)
                patch.ManualChecked = true;
            string key = Member.MakeKey(pos);
            if (_members.ContainsKey(key))
                return;
            var m = new Member
            {
                Key = key,
                Pos = pos,
                Prefab = prefab,
                RespawnMinutes = respawnMinutes,
                Picked = picked,
                PickedSec = pickedSec,
            };
            _members[key] = m;
            _owner[m] = patch;
            patch.Members.Add(m);
            patch.Recenter();
            Version++;
        }

        /// <summary>What a loaded Pickable's ZDO says about a member we track.</summary>
        public static void Observe(Vector3 pos, bool picked, double pickedSecOrZero)
        {
            Member m = Find(pos);
            if (m == null)
                return;
            bool changed = false;
            if (m.Picked != picked)
            {
                m.Picked = picked;
                changed = true;
                if (!picked)
                {
                    Patch p = PatchOf(m);
                    if (p != null)
                        p.ManualChecked = false;
                }
            }
            if (picked && pickedSecOrZero > 0.0 && Math.Abs(m.PickedSec - pickedSecOrZero) > 1.0)
            {
                m.PickedSec = pickedSecOrZero;
                changed = true;
            }
            else if (picked && m.PickedSec <= 0.0)
            {
                m.PickedSec = Now();
                changed = true;
            }
            if (changed)
                Touch();
        }

        /// <summary>The object at this spot is gone for good: mined out, burned, or a one-shot picked.</summary>
        public static bool NoteDestroyed(Vector3 pos)
        {
            Member m = Find(pos);
            if (m == null)
                return false;
            RemoveMember(m);
            Touch();
            return true;
        }

        public static void Forget(Patch patch)
        {
            for (int i = patch.Members.Count - 1; i >= 0; i--)
            {
                Member m = patch.Members[i];
                _members.Remove(m.Key);
                _owner.Remove(m);
                _forgotten.Add(m.Key);
            }
            patch.Members.Clear();
            _patches.Remove(patch);
            Touch();
        }

        public static void ToggleManualCheck(Patch patch)
        {
            patch.ManualChecked = !patch.ManualChecked;
            Touch();
        }

        /// <summary>Nearest patch to a world point within radius (XZ), any kind or item.</summary>
        public static Patch NearestAny(Vector3 pos, float radius)
        {
            Patch best = null;
            float bestD = radius;
            for (int i = 0; i < _patches.Count; i++)
            {
                float d = Utils.DistanceXZ(_patches[i].Center, pos);
                if (d <= bestD)
                {
                    bestD = d;
                    best = _patches[i];
                }
            }
            return best;
        }

        private static void RemoveMember(Member m)
        {
            _members.Remove(m.Key);
            Patch p;
            if (_owner.TryGetValue(m, out p))
            {
                _owner.Remove(m);
                p.Members.Remove(m);
                if (p.Members.Count == 0)
                    _patches.Remove(p);
                else
                    p.Recenter();
            }
        }

        private static Patch Nearest(ResourceKind kind, string item, Vector3 pos, float radius)
        {
            Patch best = null;
            float bestD = radius;
            for (int i = 0; i < _patches.Count; i++)
            {
                Patch p = _patches[i];
                if (p.Kind != kind || p.Item != item)
                    continue;
                float d = Utils.DistanceXZ(p.Center, pos);
                if (d <= bestD)
                {
                    bestD = d;
                    best = p;
                }
            }
            return best;
        }

        private static void Touch()
        {
            Dirty = true;
            Version++;
        }
    }
}
