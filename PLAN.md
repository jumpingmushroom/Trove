# Trove — Technical Plan (draft for discussion)

**Goal:** a client-side Valheim mod that remembers where you gathered. When you pick a wild
pickable (berries, thistle, mushrooms, …) or put a pickaxe into an ore node, the spot becomes a
map pin: one pin per patch with a count and the item's own icon, dimmed while the patch is picked
clean, back to normal when it has regrown, gone when the ore is mined out. Nothing is scanned,
nothing is revealed; the map only ever shows what you have touched.

**Name:** Trove (`com.jumpingmushroom.trove`). Chosen 2026-09-22 over the working name
GatherBuddy, which collides with a well-known FFXIV plugin. See §4.8.

**Target build:** Valheim **1.0.15** (`Version.CurrentVersion = new GameVersion(1, 0, 15)`), the
assembly pulled from the rig on 2026-09-22 into `lib/`. The analysis below was done on the
1.0.14 assembly in `../Recount/lib` (dated 2026-09-17); every member cited was re-checked
against 1.0.15 before the first build and none had changed. Line-level evidence is in
`docs/game-code-findings.md`, the market and mechanics research in `docs/RESEARCH.md`.

---

## 1. What the game actually does

### 1.1 Picking happens on the picker's client, once

`Player.Update` runs only on the owner of the player (`Player.cs:871`), and the Use key ends in
`Pickable.Interact(Humanoid, bool, bool)` (`Pickable.cs:180-228`). That method executes **only on
the picking client**; it sends the owner-targeted RPC `RPC_Pick`, the owner drops the items and
broadcasts `RPC_SetPicked(true)`, which every client applies in `SetPicked` (269-293).

So a Harmony prefix on `Pickable.Interact` guarded by
`!__instance.GetPicked() && __instance.GetEnabled == 1 && character == Player.m_localPlayer`
fires exactly once per successful pick, on our client only, with everything the mod needs on the
instance: `transform.position`, `m_itemPrefab.name` (e.g. `Raspberry`), `GetHoverName()` (the
`$item_…` token), `m_amount`, and **`m_respawnTimeMinutes`**. That last field is the whole
lifecycle model: `0` means the pickable never respawns (wild seeds; the object is destroyed on
pick, 291), anything else is the regrow time. It lives on the prefab, so the mod needs no
respawn table and Deep North or modded pickables work untouched.

`SetPicked` runs on every client for every loaded pickable, whoever picked it, and on respawn
(`UpdateRespawn` → `RPC_SetPicked(false)`, 132-178). That is a free correction signal: if a
tracked bush is picked by someone else, or regrows, we see it while it is loaded.

`picked_time` in the ZDO is DateTime ticks of `ZNet.instance.GetTime()`, written **by the owner**
(285-287), possibly up to a minute late. Respawn ETA is therefore computed from
`ZNet.instance.GetTimeSeconds()` at our own pick and only *corrected* from the ZDO.

`PickableItem` (loose stones, branches, flint) is destroyed on pick and never respawns
(`PickableItem.cs:113-122`): ignored.

Cultivated crops grow via `Plant.Grow()` into the same `Pickable` prefabs as the wild ones
(`Plant.cs:181-206`), so a carrot harvest looks exactly like a wild pick.
`EffectArea.IsPointInsideArea(pos, EffectArea.Type.PlayerBase)` (`EffectArea.cs:197`, verified)
answers "inside a workbench radius", which is the cheap farm filter.

### 1.2 Mining: the hit is ours, the depletion is the owner's

`MineRock5`, `MineRock` and `Destructible` implement `IDestructible.Damage(HitData)`, which runs
on the **attacker's** client and forwards to an owner-only RPC (`MineRock5.cs:286-357`,
`MineRock.cs:103-219`, `Destructible.cs:92-171`). `HitData.m_attacker` is set by `Attack` and
`Projectile`; `hit.GetAttacker() == Player.m_localPlayer` is the game's own attribution test (used
for the mining stats, `MineRock5.cs:400`). `hit.m_skill` is the skill of the weapon
(`Skills.SkillType.Pickaxes` for a pickaxe; Recount relies on the same field). Tool tier is
only checked inside the owner RPC (`HitData.CheckToolTier`, `HitData.cs:1083`), so a wooden club
bouncing off silver still reaches `Damage` — repeat the check in the prefix.

Depletion:

- `MineRock5` keeps every piece's health in one ZDO string (`s_health`, Base64 `ZPackage`:
  `int count` then floats; `LoadHealth` 174, `SaveHealth` 194). When a piece dies the owner
  broadcasts `RPC_SetAreaHealth(idx, hp)` to everyone (406) and, when all are dead, calls
  `m_nview.Destroy()` (413-417). Non-owners can decode `s_health` for a "% mined" figure.
- `Destructible` has a single `health`; on death `Destroy(hit)` instantiates
  `m_spawnWhenDestroyed` and **re-applies the same hit to its `MineRock5`** (219), then
  `ZNetScene.instance.Destroy(gameObject)`. Copper and silver are believed to be exactly this: a
  `Destructible` shell whose replacement is the `*_frac` `MineRock5` [assumption, asset data].
  One swing can therefore produce two `Damage` calls at the same position.
- `MineRock` (tin, obsidian per the wiki [assumption]) stores `"Health"+i` floats and destroys
  itself when `AllDestroyed()` (187-190).
- No node ever respawns; all three destroy the ZDO. Flametal veins sink with the Lavaiathan, which
  also ends in the object vanishing.

Plain boulders are `MineRock5` too, dropping only `Stone`, so "is a MineRock5" is not "is ore".
Drop tables are static data: `m_dropItems.m_drops[i].m_item.name` on MineRock/MineRock5,
`DropOnDestroyed.m_dropWhenDestroyed` on the sibling of a Destructible (`DropOnDestroyed.cs:8`).

### 1.3 Pins

`Minimap.AddPin(pos, PinType, name, save, isChecked, ownerID = 0, PlatformUserID author = default)`
(`Minimap.cs:2352-2385`) is public; there is **no pin limit** in the code. `PinData.m_icon` is a
public `Sprite`, copied into the marker only when the marker is created (1658), so a custom icon
is "set `m_icon` right after creating the pin". `m_checked` shows the vanilla cross (1700-1703).
Names render only on the large map below zoom 0.5 (`m_showNamesZoom`, 244, 1704-1711).

Saved pins (`m_save`) are written into the character's `.fch` per world UID (2164-2177,
`PlayerProfile.cs:671-678`) **and** exported to the cartography table — every saved type except
Death (2734-2757). The table has a known, unfixed bug (since Ashlands, still reported Sept 2025)
where deleted pins are resurrected on the next Record/Read. Auto-pins must therefore be
**`save:false`**, which also makes them invisible to the private `GetClosestPin` (2273-2290): the
game's own right-click delete and left-click check will not touch them, so the mod handles its
own clicks. PortalLines already builds `PinData` by hand to avoid `AddPin`'s `PlatformUserID`
parameter dragging in `Splatform.dll` (`PortalLines/src/PortalLines/UI/PortalPins.cs:138-156`);
the same helper is reused here.

`PinType.None` pins sit outside the vanilla icon filter (`m_visibleIconTypes`), so visibility is
the mod's own toggle. Jotunn 2.30.x `MinimapManager` has no pin API, only overlays and the events
`OnVanillaMapAvailable` / `OnVanillaMapDataLoaded`; Jotunn is not needed.

### 1.4 Identity, world, time

`ZNet.instance.GetWorldUID()` (`ZNet.cs:2170`; `World.m_uid = name hash + generated id`,
`World.cs:74`) identifies the world; `Player.GetPlayerID()` the character. `ZNet.instance.
GetTimeSeconds()` is server-synced game time; `EnvMan.instance.m_dayLengthSec` (1200) turns it into
days for display. Item icons: `ItemDrop.m_itemData.GetIcon()` on the item prefab.

---

## 2. Proposed design

Namespaces mirror the siblings: `Core`, `Model`, `Patches`, `UI`. Client-only, no ServerSync,
no server component, no Jotunn.

### 2.1 Catalogue (`Core/ResourceCatalog`)

Built once per world from `ZNetScene.instance.m_prefabs`, nothing hard-coded:

- **Pickable, renewable**: has `Pickable` with `m_respawnTimeMinutes > 0`. Tracked.
- **Pickable, one-shot**: `m_respawnTimeMinutes == 0` (wild seeds, village flax/barley). Not
  tracked by default (config `Gather.PinOneShot` to pin them as a plain "found here" marker).
- **Ore node**: has `MineRock5`, `MineRock`, or `Destructible` (+ `DropOnDestroyed` or a
  `m_spawnWhenDestroyed` carrying `MineRock5`), and its drop table yields at least one item not
  in `Mine.IgnoredDrops` (default `Stone`). The pin's icon and label come from the first such
  item (CopperOre, TinOre, SilverOre, IronScrap, Obsidian, BlackMarble, FlametalOre, …).
- Everything else is ignored. The catalogue is logged at first world load (`trove catalog`
  console command prints it) so the [assumption]s about tin/copper composition are checked on
  the rig, not guessed.

### 2.2 Capture (`Patches/GatherPatches`, `Patches/MinePatches`)

- `Pickable.Interact` **prefix**: the guard from §1.1, then `PatchStore.RecordPick(prefab, item,
  pos, respawnMinutes, now)`. Skipped when `Gather.SkipPlayerBases` and the position is inside a
  `PlayerBase` effect area, or the item is in `Gather.ExcludedItems`.
- `Pickable.SetPicked` **postfix** (every client, every loaded pickable): if the pickable's
  rounded position is a tracked member, update its picked flag and, when `picked`, its ready
  time from the ZDO if present. This is what keeps counts honest in multiplayer and after
  regrowth without any scanning.
- `MineRock5.Damage`, `MineRock.Damage`, `Destructible.Damage` **prefix**: guard
  `hit.GetAttacker() == Player.m_localPlayer && hit.m_skill == Skills.SkillType.Pickaxes &&
  hit.CheckToolTier(__instance.m_minToolTier)` and catalogue says ore → `PatchStore.RecordMine`.
  Keyed by rounded position so the Destructible shell and its `_frac` replacement are one entry.
  (`hit.m_skill == Pickaxes` is the [assumption] to verify first; fallback is the
  `Skills.SkillType` of `Player.m_localPlayer.GetCurrentWeapon()`.)
- `MineRock5.RPC_SetAreaHealth` **postfix**: decode `s_health`, store `% destroyed` on the entry;
  at `Mine.DepletedPercent` (default 90) mark it depleted.
- `ZNetScene.Destroy(GameObject)` **prefix**: if the object carries a tracked component at a
  tracked position, mark the entry destroyed (ore mined out, berry bush burned, seed picked).
  [Verify: the unload path `ZNetScene.RemoveObjects` uses `Object.Destroy`, not this method, so
  leaving the area must not trigger it. Log at first deploy.]

### 2.3 Patches and clustering (`Model/Patch`, `Core/PatchStore`)

One in-memory table of **patches** keyed by item + rounded position. A pick within
`Gather.ClusterRadius` (default 20 m; thistle grows in groups of 1–5, berry rows in Meadows are
within a few metres) of an existing patch of the same item joins it as a **member** (position
rounded to 0.5 m, respawn minutes, last picked game-seconds, picked flag). Otherwise a new patch
is created at the first member's position; the pin sits at the members' centroid.

Ore nodes are their own patches (one deposit, one pin) unless `Mine.ClusterRadius` (default
12 m, tin along a shoreline) merges them.

Derived per patch: `Count` (members), `Ready` (members not picked or whose ready time has
passed), `NextReadySec`. A patch is **checked** (crossed out) when `Ready == 0`; ore patches are
**removed** when depleted or destroyed. Everything here is positions and numbers; ZDOIDs are
never persisted (PortalLines PLAN §1.3 learned this the hard way).

### 2.4 Pins (`UI/ResourcePins`)

Reuses the PortalLines pattern: one hand-built `PinData` per patch, `save:false`,
`PinType.None`, `m_icon` = the item sprite, name = `Localization.instance.Localize(itemToken)`
plus ` ×N` when `N > 1` (config `Pins.ShowCounts`), `m_checked` = crossed. Sync runs on a
version counter from the store, at most a few times a second, and only sets
`m_pinUpdateRequired` when something changed. Rebuilt on `Minimap` instance change and after the
vanilla map data loads (postfix `Minimap.LoadMapData`), since the game never persists these.

Interaction, because vanilla ignores unsaved pins:

- **Right-click** on the large map (`Minimap.OnMapRightClick` prefix, own nearest-pin search
  within `PinInteractRadius`): forget the patch. A tombstone (item + position, `Pins.Forgotten`)
  stops the next pick from re-creating it; `trove unforget` clears tombstones.
- **Hover** (PortalLines `PortalHover` pattern): tooltip with members, "3 of 6 ready", and
  "next in 1d 4h" using `m_dayLengthSec`.
- **Left-click**: toggle manual check, cleared automatically when a member regrows.
- **Visibility**: `Pins.Enabled`, `Pins.ShowOnMinimap` (off by default — the small map draws
  every marker), `Pins.HideChecked`, hotkey to toggle (default none; PortalLines uses a map
  button, same here).

Toast on a **new** patch only: `$msg_pin_added`-style HUD message with the icon
(`MessageHud.instance.ShowMessage(TopLeft, …)`), config `Pins.Toast`.

### 2.5 Persistence (`Core/PatchCache`)

Per-world file, PortalLines style: `BepInEx/config/Trove/<worldUID>.tsv`, header line with
a format version, one line per member (`item\tx\ty\tz\tpatchX\tpatchZ\trespawnMin\tpickedSec\tpicked\tkind\tflags`)
so it is greppable and needs no serializer. Loaded when `ZNet.World` is known, saved debounced
(a few seconds after a change) and on `Game.SavePlayerProfile` postfix and world exit.
Shared by every character on that world by design (decision 4). `trove export` writes a
human-readable list; `trove clear` wipes the world's cache after confirmation.

### 2.6 Config

`Gather` (enabled, cluster radius, skip player bases, excluded items, pin one-shot),
`Mine` (enabled, cluster radius, depleted percent, ignored drops),
`Pins` (enabled, show on minimap, show counts, hide checked, toast, icon size),
`Keys` (toggle pins). All ConfigurationManager-friendly like the siblings.

### 2.7 Compatibility

- Radar pinners (Cartur's, BetterMap, Hex, …) coexist: ours are unsaved and typed `None`, theirs
  are saved and typed. Nothing is written to the cartography table, so shared maps stay clean.
- PortalLines hover/click code lives in the same map space; both use their own nearest-pin
  search on unsaved pins, so a hit in one must not also fire in the other — check
  `PinInteractRadius` ordering on first deploy.
- Modded pickables and ores are picked up by the catalogue automatically.
- Dedicated servers need nothing; the mod never sends an RPC.

### 2.8 Runtime assumptions to verify on first deploy (log them)

1. Which component copper (`rock4_copper`), tin (`MineRock_Tin`), silver, obsidian, mud piles,
   giant remains and flametal actually carry, and that their drop tables classify as ore.
2. `hit.m_skill == Skills.SkillType.Pickaxes` for pickaxe hits on all three node types.
3. `ZNetScene.Destroy` is not called on area unload.
4. `Pickable.Interact` prefix fires once per pick with hold-to-repeat, and not for tar-stuck picks.
5. `ItemDrop.m_itemData.GetIcon()` is non-null for every catalogued item at world load.
6. `EffectArea.IsPointInsideArea(pos, PlayerBase)` returns the workbench area for a crop inside
   a fenced farm.
7. Pin markers survive map zoom/pan without flicker at ~200 patches (the realistic ceiling
   with clustering).

---

## 3. Phases

| Version | Scope |
|---|---|
| 0.1 | Pickable capture, clustering, per-world cache, item-icon pins with counts, farm skip, catalogue log. |
| 0.2 | Ore: pickaxe-hit capture, depletion via `RPC_SetAreaHealth` and `ZNetScene.Destroy`, pin removal. |
| 0.3 | Lifecycle: `SetPicked` corrections, checked state, hover tooltip with ready times, right-click forget with tombstones, left-click check. |
| 0.4 | Polish: toasts, minimap option, console export/clear, README, Thunderstore release. |
| later | Gather run (route through ready patches via PortalLines' planner), HUD arrow to nearest ready patch, yield memory per patch. |

## 4. Decisions (taken 2026-09-22)

1. **Scope: ore and pickables both.** Pickables ship first (0.1), ore in 0.2.
2. **One pin per patch with a count.** Cluster radius 20 m pickables / 12 m ore, configurable.
3. **Item icons.** `m_icon` = the item's sprite; re-applied on every rebuild since the game never
   saves it. Vanilla icon filter does not apply; the mod has its own toggle.
4. **Per-world cache file** in `BepInEx/config/<Mod>/<worldUID>.tsv`, shared by all characters on
   that world, PortalLines format.
5. **Trigger: interaction only, always.** No proximity scan, no hotkey scan, no exceptions.
6. **Pins are unsaved** (`save:false`): never in the `.fch`, never on a cartography table.
7. **No Jotunn dependency.** BepInEx 5 + HarmonyX only.
8. **Name: Trove** (`com.jumpingmushroom.trove`). A trove is a store of found things, which
   covers ore and berries alike, and it is one word like the sibling mods. The working name
   GatherBuddy collides with a well-known FFXIV gathering plugin, which costs searchability.
   Checked against the full Thunderstore package list on 2026-09-22: no package contains
   "trove". Runners-up were Findings and Gleanings.
