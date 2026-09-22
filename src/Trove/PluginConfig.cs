using System;
using BepInEx.Configuration;

namespace Trove
{
    public static class PluginConfig
    {
        public static ConfigEntry<bool> GatherEnabled;
        public static ConfigEntry<float> GatherClusterRadius;
        public static ConfigEntry<bool> SkipPlayerBases;
        public static ConfigEntry<string> ExcludedItems;
        public static ConfigEntry<bool> PinOneShot;

        public static ConfigEntry<bool> MineEnabled;
        public static ConfigEntry<float> MineClusterRadius;
        public static ConfigEntry<string> IgnoredDrops;
        public static ConfigEntry<string> ExcludedPrefabs;
        public static ConfigEntry<int> DepletedPercent;

        public static ConfigEntry<bool> PinsEnabled;
        public static ConfigEntry<bool> ShowOnMinimap;
        public static ConfigEntry<bool> ShowCounts;
        public static ConfigEntry<bool> HideChecked;
        public static ConfigEntry<bool> Toast;
        public static ConfigEntry<KeyboardShortcut> ToggleKey;
        public static ConfigEntry<bool> HoverEnabled;
        public static ConfigEntry<bool> ClickEnabled;

        public static ConfigEntry<bool> Verbose;

        /// <summary>Raised when something that changes which pins exist or how they read is edited.</summary>
        public static event Action PinsChanged;

        private static ConfigurationManagerAttributes Attr(int order, bool advanced = false)
        {
            return new ConfigurationManagerAttributes { Order = order, IsAdvanced = advanced };
        }

        public static void Bind(ConfigFile cfg)
        {
            GatherEnabled = cfg.Bind("Gather", "Enabled", true,
                new ConfigDescription(
                    "Remember every wild pickable you pick: berries, mushrooms, thistle, dandelions and " +
                    "anything else that grows back. Only what you actually pick; nothing is scanned.",
                    null, Attr(100)));

            GatherClusterRadius = cfg.Bind("Gather", "ClusterRadius", 20f,
                new ConfigDescription(
                    "Picks of the same item within this many metres of a patch join it instead of " +
                    "making a new pin.",
                    new AcceptableValueRange<float>(2f, 100f), Attr(95)));

            SkipPlayerBases = cfg.Bind("Gather", "SkipPlayerBases", true,
                new ConfigDescription(
                    "Ignore picks inside a workbench radius, so harvesting your own farm does not pin it.",
                    null, Attr(90)));

            ExcludedItems = cfg.Bind("Gather", "ExcludedItems", "Wood,Frostwood,Flint",
                new ConfigDescription(
                    "Comma-separated item prefab names never to pin. Branches and flint on the " +
                    "ground do grow back, but nobody wants a pin per stick.",
                    null, Attr(85)));

            PinOneShot = cfg.Bind("Gather", "PinOneShot", false,
                new ConfigDescription(
                    "Also pin pickables that never grow back (wild seeds). Off: they are ignored.",
                    null, Attr(80)));

            MineEnabled = cfg.Bind("Mine", "Enabled", true,
                new ConfigDescription(
                    "Remember every ore node you put a pickaxe into. The pin goes when the node is gone.",
                    null, Attr(70)));

            MineClusterRadius = cfg.Bind("Mine", "ClusterRadius", 12f,
                new ConfigDescription(
                    "Nodes yielding the same ore within this many metres share one pin.",
                    new AcceptableValueRange<float>(0f, 60f), Attr(65)));

            IgnoredDrops = cfg.Bind("Mine", "IgnoredDrops", "Stone,Grausten,Wood,Frostwood,Coal",
                new ConfigDescription(
                    "Comma-separated item prefab names that do not make a rock worth remembering. " +
                    "A node is an ore node when it drops anything not in this list. The defaults " +
                    "are the building materials that are everywhere: without them every Ashlands " +
                    "cliff and every Deep North stump is a deposit.",
                    null, Attr(60)));

            ExcludedPrefabs = cfg.Bind("Mine", "ExcludedPrefabs", "IceShard_*,Rock_destructible_test",
                new ConfigDescription(
                    "Comma-separated node prefab names never to pin, even when they yield something " +
                    "worth keeping. A trailing * matches any ending, so IceShard_* covers all six " +
                    "Deep North shards. Use this for things that are scattered everywhere rather " +
                    "than worth walking back to.",
                    null, Attr(58)));

            DepletedPercent = cfg.Bind("Mine", "DepletedPercent", 90,
                new ConfigDescription(
                    "Forget a deposit once this share of its pieces is mined. Copper and silver " +
                    "keep a few pieces below the dig limit, so 100 would never trigger.",
                    new AcceptableValueRange<int>(50, 100), Attr(55)));

            PinsEnabled = cfg.Bind("Pins", "Enabled", true,
                new ConfigDescription(
                    "Show the remembered patches on the large map. These pins are never saved into " +
                    "the character file and never shared through the cartography table.",
                    null, Attr(50)));

            ShowOnMinimap = cfg.Bind("Pins", "ShowOnMinimap", false,
                new ConfigDescription("Also show them on the small minimap in the corner.", null, Attr(45)));

            ShowCounts = cfg.Bind("Pins", "ShowCounts", true,
                new ConfigDescription("Append the number of plants or nodes in the patch to the pin name.", null, Attr(40)));

            HideChecked = cfg.Bind("Pins", "HideChecked", false,
                new ConfigDescription(
                    "Hide a patch entirely while everything in it is picked, instead of crossing it out.",
                    null, Attr(35)));

            Toast = cfg.Bind("Pins", "Toast", true,
                new ConfigDescription("Show a message in the top-left when a new patch is remembered.", null, Attr(30)));

            ToggleKey = cfg.Bind("Pins", "ToggleKey", KeyboardShortcut.Empty,
                new ConfigDescription("Hotkey that shows and hides the pins. Unbound by default.", null, Attr(25)));

            HoverEnabled = cfg.Bind("Pins", "Hover", true,
                new ConfigDescription(
                    "Hovering a patch on the large map shows what is in it, how much is ready and " +
                    "when the rest grows back.",
                    null, Attr(22)));

            ClickEnabled = cfg.Bind("Pins", "Click", true,
                new ConfigDescription(
                    "Left-click a patch to cross it out by hand (clears itself when something " +
                    "regrows); right-click to forget it. A forgotten patch is not re-pinned by " +
                    "later picks until 'trove unforget'.",
                    null, Attr(20)));

            Verbose = cfg.Bind("Logging", "Verbose", false,
                new ConfigDescription("Log every pick, hit and pin change to the BepInEx log.", null, Attr(5, advanced: true)));

            PinsEnabled.SettingChanged += (s, e) => Raise();
            ShowOnMinimap.SettingChanged += (s, e) => Raise();
            ShowCounts.SettingChanged += (s, e) => Raise();
            HideChecked.SettingChanged += (s, e) => Raise();
        }

        private static void Raise()
        {
            Action a = PinsChanged;
            if (a == null)
                return;
            try
            {
                a();
            }
            catch (Exception ex)
            {
                TrovePlugin.Log.LogWarning("config listener threw: " + ex);
            }
        }
    }
}
