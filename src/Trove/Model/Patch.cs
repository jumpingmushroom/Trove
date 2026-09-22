using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Trove.Model
{
    public enum ResourceKind
    {
        None,

        /// <summary>A Pickable that grows back (m_respawnTimeMinutes > 0).</summary>
        Pickable,

        /// <summary>A Pickable that never grows back (wild seeds).</summary>
        OneShot,

        /// <summary>A rock that drops something other than stone when mined.</summary>
        Ore
    }

    /// <summary>One plant or one ore node, identified by its rounded world position.</summary>
    public sealed class Member
    {
        public string Key = "";
        public Vector3 Pos;
        public string Prefab = "";
        public float RespawnMinutes;
        public bool Picked;

        /// <summary>Game seconds (ZNet net time) when it was last picked; 0 if unknown.</summary>
        public double PickedSec;

        public double ReadySec => Picked ? PickedSec + RespawnMinutes * 60.0 : 0.0;

        public bool IsReady(double now)
        {
            if (!Picked)
                return true;
            if (RespawnMinutes <= 0f)
                return false;
            return now >= ReadySec;
        }

        /// <summary>0.5 m grid, XZ and Y: the same key PortalLines uses, so entries survive reloads.</summary>
        public static string MakeKey(Vector3 p)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:0.0}|{1:0.0}|{2:0.0}",
                Mathf.Round(p.x * 2f) / 2f, Mathf.Round(p.y * 2f) / 2f, Mathf.Round(p.z * 2f) / 2f);
        }
    }

    /// <summary>A cluster of members yielding the same item: one pin.</summary>
    public sealed class Patch
    {
        public int Id;
        public ResourceKind Kind;

        /// <summary>Item prefab name, e.g. "Raspberry" or "CopperOre".</summary>
        public string Item = "";

        public readonly List<Member> Members = new List<Member>();

        /// <summary>Crossed out by the player; cleared when a member grows back.</summary>
        public bool ManualChecked;

        public Vector3 Center;

        public int Count => Members.Count;

        public int ReadyCount(double now)
        {
            int n = 0;
            for (int i = 0; i < Members.Count; i++)
                if (Members[i].IsReady(now))
                    n++;
            return n;
        }

        /// <summary>Earliest time any picked member grows back; 0 when none is pending.</summary>
        public double NextReadySec(double now)
        {
            double best = 0.0;
            for (int i = 0; i < Members.Count; i++)
            {
                Member m = Members[i];
                if (!m.Picked || m.RespawnMinutes <= 0f)
                    continue;
                double r = m.ReadySec;
                if (r <= now)
                    continue;
                if (best == 0.0 || r < best)
                    best = r;
            }
            return best;
        }

        public void Recenter()
        {
            if (Members.Count == 0)
                return;
            Vector3 sum = Vector3.zero;
            for (int i = 0; i < Members.Count; i++)
                sum += Members[i].Pos;
            Center = sum / Members.Count;
        }
    }
}
