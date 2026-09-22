using Trove.Model;

namespace Trove.Core
{
    /// <summary>"trove" console command: what the mod remembers, and what it thinks the prefabs are.</summary>
    public static class ConsoleCommands
    {
        private static bool _registered;

        public static void Register()
        {
            if (_registered)
                return;
            _registered = true;

            new Terminal.ConsoleCommand("trove",
                "Trove: list | catalog | save | unforget | clear",
                delegate (Terminal.ConsoleEventArgs args)
                {
                    string sub = args.Length > 1 ? args[1].ToLowerInvariant() : "help";
                    switch (sub)
                    {
                        case "list": List(args.Context); break;
                        case "catalog": Catalog(args.Context); break;
                        case "save":
                            PatchCache.SaveIfDirty();
                            Say(args.Context, "Trove: saved to " + (PatchCache.Path ?? "(no world)"));
                            break;
                        case "unforget":
                            PatchStore.Unforget();
                            PatchCache.SaveIfDirty();
                            Say(args.Context, "Trove: forgotten spots can be pinned again.");
                            break;
                        case "clear":
                            if (args.Length > 2 && args[2].ToLowerInvariant() == "yes")
                            {
                                PatchStore.ForgetAll();
                                PatchCache.SaveIfDirty();
                                Say(args.Context, "Trove: forgot every patch for this world.");
                            }
                            else
                                Say(args.Context, "Trove: this wipes the world's cache. Type: trove clear yes");
                            break;
                        default:
                            Say(args.Context, "trove list     - every remembered patch: item, position, count, ready");
                            Say(args.Context, "trove catalog  - what each prefab classifies as");
                            Say(args.Context, "trove save     - write the cache now");
                            Say(args.Context, "trove unforget - let right-click-forgotten spots be pinned again");
                            Say(args.Context, "trove clear yes - forget everything for this world");
                            break;
                    }
                });
        }

        private static void Say(Terminal ctx, string line)
        {
            ctx.AddString(line);
            TrovePlugin.Log.LogInfo(line);
        }

        private static void List(Terminal ctx)
        {
            double now = PatchStore.Now();
            Say(ctx, string.Format("Trove: {0} patch(es), {1} member(s), {2} forgotten spot(s){3}",
                PatchStore.Patches.Count, PatchStore.MemberCount, PatchStore.ForgottenCount,
                PatchCache.Loaded ? "" : " (no world loaded)"));
            foreach (Patch p in PatchStore.Patches)
            {
                int ready = p.ReadyCount(now);
                double next = p.NextReadySec(now);
                string eta = next > 0 ? string.Format("  next in {0:0} min", (next - now) / 60.0) : "";
                Say(ctx, string.Format("  #{0} {1} {2}  ({3:0},{4:0})  x{5}  {6} ready{7}",
                    p.Id, p.Kind, p.Item, p.Center.x, p.Center.z, p.Count, ready, eta));
            }
        }

        private static void Catalog(Terminal ctx)
        {
            ResourceCatalog.EnsureBuilt();
            if (!ResourceCatalog.Built)
            {
                Say(ctx, "Trove: no world loaded, nothing to classify yet.");
                return;
            }
            int n = 0;
            foreach (ResourceCatalog.Info i in ResourceCatalog.All)
            {
                n++;
                Say(ctx, string.Format("  {0,-9} {1,-32} -> {2,-20} {3}{4}{5}",
                    i.Kind, i.Prefab, i.Item, i.Via,
                    i.RespawnMinutes > 0 ? "  respawn " + i.RespawnMinutes + " min" : "",
                    i.Icon == null ? "  (no icon)" : ""));
                if (i.Drops.Length > 0)
                    Say(ctx, "               drops: " + i.Drops);
            }
            Say(ctx, "Trove: " + n + " prefab(s) classified.");
        }
    }
}
