using Trove.UI;
using HarmonyLib;

namespace Trove.Patches
{
    /// <summary>
    /// LoadMapData rebuilds the vanilla pin list from the character file; ours are not in it,
    /// so they are re-added on the next sync.
    /// </summary>
    [HarmonyPatch(typeof(Minimap), "LoadMapData")]
    public static class Minimap_LoadMapData_Patch
    {
        private static void Postfix()
        {
            ResourcePins.InvalidateAll();
        }
    }

    /// <summary>Flush the cache whenever the game saves the character.</summary>
    [HarmonyPatch(typeof(Game), nameof(Game.SavePlayerProfile))]
    public static class Game_SavePlayerProfile_Patch
    {
        private static void Postfix()
        {
            Core.PatchCache.SaveIfDirty();
        }
    }
}
