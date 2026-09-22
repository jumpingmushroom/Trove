using System.Text;
using Trove.Core;
using Trove.Model;
using UnityEngine;

namespace Trove.UI
{
    /// <summary>
    /// The patch under the cursor on the large map, and a small panel saying what is in it,
    /// how much is ready and when the rest grows back. Uses the same radius as the map's own
    /// pin clicks, so what you can hover is what you can click.
    /// </summary>
    internal sealed class PatchHover
    {
        private readonly MapPanel _panel = new MapPanel();
        private Minimap _map;

        /// <summary>The patch under the cursor right now; the click patches read this.</summary>
        public static Patch Current { get; private set; }

        public void Update(Minimap map)
        {
            if (map == null)
            {
                Clear();
                return;
            }
            if (!ReferenceEquals(map, _map))
            {
                _panel.Destroy();
                _map = map;
            }

            if (!PluginConfig.HoverEnabled.Value && !PluginConfig.ClickEnabled.Value)
            {
                Clear();
                return;
            }
            if (Minimap.InTextInput() || !ZInput.IsMouseActive() || PatchStore.Patches.Count == 0)
            {
                Clear();
                return;
            }

            Vector3 pointer = ZInput.pointerPosition;
            Vector3 world = map.ScreenToWorldPoint(pointer);
            if (world == Vector3.zero)
            {
                Clear();
                return;
            }

            Patch best = PatchStore.NearestAny(world, map.PinInteractRadius);
            Current = best;
            if (best == null || !PluginConfig.HoverEnabled.Value)
            {
                _panel.Hide();
                return;
            }

            if (!_panel.Created && !_panel.Create(map, "TroveHover"))
                return;
            _panel.ShowAtScreen(map, Describe(best), pointer);
        }

        public void Clear()
        {
            Current = null;
            _panel.Hide();
        }

        public void Destroy()
        {
            Current = null;
            _panel.Destroy();
            _map = null;
        }

        private static string Describe(Patch p)
        {
            double now = PatchStore.Now();
            var sb = new StringBuilder(160);
            ResourceCatalog.Info info = ResourceCatalog.ForItem(p.Item);
            string name = ResourceCatalog.DisplayName(info);
            if (string.IsNullOrEmpty(name))
                name = p.Item;
            sb.Append("<b>").Append(name).Append("</b>");

            if (p.Kind == ResourceKind.Ore)
            {
                sb.Append('\n').Append(p.Count).Append(p.Count == 1 ? " node" : " nodes");
            }
            else
            {
                int ready = p.ReadyCount(now);
                sb.Append('\n').Append(ready).Append(" of ").Append(p.Count).Append(" ready");
                double next = p.NextReadySec(now);
                if (next > now)
                    sb.Append(", next in ").Append(Span(next - now));
                if (p.Kind == ResourceKind.OneShot)
                    sb.Append("\n<alpha=#99>does not grow back");
            }

            if (p.ManualChecked)
                sb.Append("\n<alpha=#99>crossed out by you");

            Player player = Player.m_localPlayer;
            if (player != null)
                sb.Append("\n<alpha=#99>").Append(Dist(Utils.DistanceXZ(player.transform.position, p.Center))).Append(" from you");

            if (PluginConfig.ClickEnabled.Value)
                sb.Append("\n<alpha=#66>left-click: cross out   right-click: forget");
            return sb.ToString();
        }

        /// <summary>Game seconds pass one per real second while the world is loaded.</summary>
        private static string Span(double sec)
        {
            if (sec < 60) return "under a minute";
            long m = (long)(sec / 60);
            if (m < 60) return m + " min";
            long h = m / 60;
            m %= 60;
            return m > 0 ? h + " h " + m + " min" : h + " h";
        }

        private static string Dist(float m)
        {
            return m < 1000f ? Mathf.RoundToInt(m) + " m" : (m / 1000f).ToString("0.0") + " km";
        }
    }
}
