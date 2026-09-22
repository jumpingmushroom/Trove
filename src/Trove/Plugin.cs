using BepInEx;
using BepInEx.Logging;
using Trove.Core;
using Trove.UI;
using HarmonyLib;
using UnityEngine;

namespace Trove
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInProcess("valheim.exe")]
    [BepInProcess("valheim.x86_64")]
    public sealed class TrovePlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.jumpingmushroom.trove";
        public const string PluginName = "Trove";
        public const string PluginVersion = "0.1.1";

        internal static ManualLogSource Log;

        private readonly ResourcePins _pins = new ResourcePins();
        private readonly PatchHover _hover = new PatchHover();
        private Harmony _harmony;

        private float _nextSave;
        private bool _hadPlayer;

        /// <summary>Never toggle while the player is typing.</summary>
        internal static bool InputBlocked()
        {
            if (Console.IsVisible()) return true;
            if (Menu.IsVisible()) return true;
            if (TextInput.IsVisible()) return true;
            if (Minimap.InTextInput()) return true;
            if (Chat.instance != null && Chat.instance.HasFocus()) return true;
            return false;
        }

        private void Awake()
        {
            Log = Logger;

            PluginConfig.Bind(base.Config);
            ConsoleCommands.Register();

            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll(typeof(TrovePlugin).Assembly);

            PluginConfig.PinsChanged += OnPinsChanged;

            Logger.LogInfo(PluginName + " " + PluginVersion + " loaded.");
        }

        private void OnDestroy()
        {
            PluginConfig.PinsChanged -= OnPinsChanged;
            _pins.Clear();
            _hover.Destroy();
            PatchCache.Unload();
            ResourceCatalog.Clear();
            if (_harmony != null)
                _harmony.UnpatchSelf();
        }

        private void OnPinsChanged()
        {
            _pins.Invalidate();
        }

        /// <summary>Logged out or returned to the menu: forget the world.</summary>
        private void LocalPlayerGone()
        {
            _pins.Clear();
            _hover.Destroy();
            PatchCache.Unload(); // saves if dirty
            ResourceCatalog.Clear();
        }

        private void Update()
        {
            Player player = Player.m_localPlayer;

            // Detect logout / world change by polling, rather than from Player.OnDestroy: that
            // method nulls m_localPlayer inside its own body, so a postfix comparing against it
            // never matches. Unity's == treats a destroyed object as null, so track presence as a
            // bool and identity with ReferenceEquals.
            bool hasPlayer = player != null;
            if (!hasPlayer && _hadPlayer)
                LocalPlayerGone();
            _hadPlayer = hasPlayer;

            if (player == null)
                return;

            ResourceCatalog.EnsureBuilt();
            PatchCache.EnsureLoaded();

            if (PluginConfig.ToggleKey.Value.IsDown() && !InputBlocked())
                PluginConfig.PinsEnabled.Value = !PluginConfig.PinsEnabled.Value;

            _pins.Sync();

            Minimap map = Minimap.instance;
            if (map != null && map.m_mode == Minimap.MapMode.Large && PluginConfig.PinsEnabled.Value)
                _hover.Update(map);
            else
                _hover.Clear();

            if (Time.time >= _nextSave)
            {
                _nextSave = Time.time + 10f;
                PatchCache.SaveIfDirty();
            }
        }
    }
}
