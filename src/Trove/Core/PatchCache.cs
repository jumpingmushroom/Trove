using System;
using System.Globalization;
using System.IO;
using System.Text;
using Trove.Model;
using UnityEngine;

namespace Trove.Core
{
    /// <summary>
    /// Per-world memory on disk. One tab-separated line per member, grouped by patch id, no
    /// serializer dependency. Lives in BepInEx/config/Trove/, so it follows the profile,
    /// and is shared by every character on that world. Positions only, never ZDOIDs.
    /// </summary>
    public static class PatchCache
    {
        private const string Header = "# Trove cache v1: patch\tkind\titem\tprefab\tx\ty\tz\trespawnMin\tpicked\tpickedSec\tflags   (kind X = forgotten spot, only x y z used)";

        private static string _path;
        private static long _loadedWorld;

        public static bool Loaded => _path != null;
        public static string Path => _path;

        /// <summary>Load the cache for the current world once ZNet knows which world that is.</summary>
        public static void EnsureLoaded()
        {
            World world = ZNet.World;
            if (world == null)
                return;

            long id = world.m_uid != 0 ? world.m_uid : world.m_seed;
            if (_path != null && _loadedWorld == id)
                return;

            Unload();
            _loadedWorld = id;
            _path = PathFor(world, id);
            Load();
        }

        public static void Unload()
        {
            if (PatchStore.Dirty)
                Save();
            PatchStore.Clear();
            _path = null;
            _loadedWorld = 0;
        }

        public static void SaveIfDirty()
        {
            if (PatchStore.Dirty)
                Save();
        }

        private static string PathFor(World world, long id)
        {
            var sb = new StringBuilder();
            foreach (char c in world.m_name ?? "world")
                sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_');
            if (sb.Length == 0)
                sb.Append("world");
            string dir = System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "Trove");
            return System.IO.Path.Combine(dir, sb + "-" + id.ToString(CultureInfo.InvariantCulture) + ".tsv");
        }

        private static void Load()
        {
            PatchStore.Clear();
            if (!File.Exists(_path))
            {
                TrovePlugin.Log.LogInfo("no cache yet at " + _path);
                return;
            }

            int bad = 0;
            try
            {
                foreach (string raw in File.ReadAllLines(_path))
                {
                    if (raw.Length == 0 || raw[0] == '#')
                        continue;
                    string[] f = raw.Split('\t');
                    if (f.Length < 10)
                    {
                        bad++;
                        continue;
                    }
                    ResourceKind kind;
                    switch (f[1])
                    {
                        case "P": kind = ResourceKind.Pickable; break;
                        case "S": kind = ResourceKind.OneShot; break;
                        case "O": kind = ResourceKind.Ore; break;
                        case "X":
                            PatchStore.RestoreForgotten(Member.MakeKey(new Vector3(F(f[4]), F(f[5]), F(f[6]))));
                            continue;
                        default: bad++; continue;
                    }
                    string flags = f.Length > 10 ? f[10] : "";
                    PatchStore.Restore(
                        int.Parse(f[0], CultureInfo.InvariantCulture),
                        kind, f[2], f[3],
                        new Vector3(F(f[4]), F(f[5]), F(f[6])),
                        F(f[7]),
                        f[8] == "1",
                        double.Parse(f[9], CultureInfo.InvariantCulture),
                        flags.IndexOf('c') >= 0);
                }
            }
            catch (Exception ex)
            {
                TrovePlugin.Log.LogWarning("could not read cache " + _path + ": " + ex.Message);
                PatchStore.Clear();
                return;
            }
            PatchStore.MarkSaved();

            TrovePlugin.Log.LogInfo(string.Format("loaded {0} patch(es), {1} member(s) from {2}{3}",
                PatchStore.Patches.Count, PatchStore.MemberCount, System.IO.Path.GetFileName(_path),
                bad > 0 ? ", skipped " + bad + " bad line(s)" : ""));
        }

        private static void Save()
        {
            if (_path == null)
                return;

            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path));
                var sb = new StringBuilder();
                sb.Append(Header).Append('\n');
                foreach (Patch p in PatchStore.Patches)
                {
                    string kind = p.Kind == ResourceKind.Ore ? "O" : p.Kind == ResourceKind.OneShot ? "S" : "P";
                    for (int i = 0; i < p.Members.Count; i++)
                    {
                        Member m = p.Members[i];
                        sb.Append(p.Id.ToString(CultureInfo.InvariantCulture)).Append('\t')
                          .Append(kind).Append('\t')
                          .Append(p.Item).Append('\t')
                          .Append(m.Prefab).Append('\t')
                          .Append(S(m.Pos.x)).Append('\t').Append(S(m.Pos.y)).Append('\t').Append(S(m.Pos.z)).Append('\t')
                          .Append(S(m.RespawnMinutes)).Append('\t')
                          .Append(m.Picked ? '1' : '0').Append('\t')
                          .Append(m.PickedSec.ToString("0.#", CultureInfo.InvariantCulture)).Append('\t')
                          .Append(p.ManualChecked ? "c" : "")
                          .Append('\n');
                    }
                }
                foreach (string key in PatchStore.Forgotten)
                {
                    // key is "x|y|z" on the 0.5 m grid; store it as coordinates like everything else
                    string[] c = key.Split('|');
                    if (c.Length != 3)
                        continue;
                    sb.Append("0\tX\t\t\t").Append(c[0]).Append('\t').Append(c[1]).Append('\t').Append(c[2])
                      .Append("\t0\t0\t0\t\n");
                }

                // Write beside, then rename: a crash mid-write must not lose the whole cache.
                string tmp = _path + ".tmp";
                File.WriteAllText(tmp, sb.ToString());
                if (File.Exists(_path))
                    File.Delete(_path);
                File.Move(tmp, _path);
                PatchStore.MarkSaved();

                if (PluginConfig.Verbose.Value)
                    TrovePlugin.Log.LogDebug("saved " + PatchStore.MemberCount + " member(s) to " + System.IO.Path.GetFileName(_path));
            }
            catch (Exception ex)
            {
                TrovePlugin.Log.LogWarning("could not write cache " + _path + ": " + ex.Message);
            }
        }

        private static string S(float v)
        {
            return v.ToString("0.##", CultureInfo.InvariantCulture);
        }

        private static float F(string s)
        {
            return float.Parse(s, CultureInfo.InvariantCulture);
        }
    }
}
