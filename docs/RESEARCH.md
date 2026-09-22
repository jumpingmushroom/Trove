# Trove — research (2026-09-22)

Question: a Valheim mod that automatically pins the spot when you **actually** pick a wild
pickable (berries, thistle, mushrooms…) or mine an ore node, so you can find it again. Does
it exist, is it worth making, what must be decided first?

Sources: Thunderstore package API + READMEs, GitHub, Steam forums, the weirdgloop/Fandom wikis,
official patch notes, and the decompiled Valheim 1.0.14 `assembly_valheim.dll` (line-level
citations in `game-code-findings.md`; decompiled types were saved in the session scratchpad).
Nexus Mods and Reddit block automated fetching, so anything Nexus-only or Reddit-only is
flagged unverified.

---

## 1. Verdict

**Worth making. The niche is unoccupied.** Every maintained 1.0-era auto-pinner is a proximity
radar. Nothing client-only, still maintained, pins pickables strictly on interaction and
tracks their respawn. The closest existing mods each miss on one axis:

| Mod | Trigger | Pickables | Lifecycle (respawn / depletion) | Client-only | Status Sept 2026 |
|---|---|---|---|---|---|
| **The Greatest Map** (DeathMonger) | look-at / crosshair / interact, "no radar" | yes | not documented | **no — server + all clients** | v1.2.0, 2026-09-20 |
| **DiscoveryPins** (Searica) | ore on damage; dungeons on enter; hotkey FOV scan | **no** (open feature requests since Jan 2025) | none | yes (Jotunn) | v0.4.1, 2026-09-20 |
| **AutoPinOres** (Pncle, Nexus) | crosshair hover | ores only | no mined-out removal | yes | 2026-09-15 |
| **AutoPin** (RookieBiscuit, Nexus 3754) | **proximity** | yes, full list | best in class: cross-out when picked clean, un-cross on regrow, ore removed at 90 % mined, seeds dropped | yes | 1.0 status unverified |
| **QoL Pins** (Tekla) | ore on damage, removed on destroy | no | ore only | yes | dead since 2021 |
| Cartur's Map Pins, BetterMap, HexResourceTracker, TrailLedger, OneMapToRuleThemAll, Huginn Map, RavenMap, Automatics, Wayfinder, OreFinder, Pinsanity, Sully's, abfielder AutoMapPins | radius scan (10–2000 m) | mostly yes | varies; Hex/Cartur/Huginn remove depleted ore | mostly | several updated 2026-09-1x |

Signals that the "interaction-only" position is a real differentiator, not just a smaller
feature set:

- Three 2026 mods market *against* radar (The Greatest Map "no radar, nothing is ever revealed
  for you"; TrailLedger "does not scan unexplored world data"; Wayfinder "without turning it
  into a global reveal"). Modders treat radar as borderline cheating: AMPED's config is literally
  named "Always Show Pins (for the cheaters amongst us)", Huginn only pins silver once you strike
  it (to keep the Wishbone relevant), AutoPin's README says "stick with Hover if that feels
  like cheating".
- Radar mods generate clutter bad enough that clean-up mods exist (PinRemoval, MapPinDeclutter,
  Pintervention). Cartur ships berries/mushrooms **off by default** because they "carpet the
  map and bloat your save". Steam reports of ~15 FPS on the zoomed-out map after mass pinning,
  and "a literal 1000 pins on my map".
- Manual pinning of berry patches is a widely repeated Steam "pro tip" (one-letter tags
  "R, B, T", "RB/BB/TH/MR"), i.e. the habit the mod automates is real.
- Iron Gate's stated intent is that non-metal resources require exploration (why berries can't
  be planted). Pinning only what you found by exploring respects that; radar does not.

Counter-arguments to weigh: some players say natural spawns are plentiful enough that marking is
pointless before mid-game; and AutoPin (Nexus) already has the best lifecycle design, so if it
turns out to work on 1.0 the gap is "interaction-gated + Thunderstore + no clutter", not
"lifecycle".

Naming: "Trove" is also the name of a very well-known FFXIV gathering plugin
(Ottermandias). No Thunderstore clash was found, but searchability suffers. Alternatives to
consider before publishing: Forager's Memory, Gathered, Pickings, Berry Trail, Trailmarks.

---

## 2. What the game does (verified in the 1.0.14 assembly)

Full detail with line numbers: `game-code-findings.md`. The parts the design rests on:

**Pickables**
- `Pickable.Interact(Humanoid, bool, bool)` runs **only on the picking client**. It then sends
  the owner-targeted RPC `RPC_Pick`; the owner drops the items and broadcasts `RPC_SetPicked`.
  So a Harmony prefix on `Interact` guarded by `!GetPicked() && character == Player.m_localPlayer`
  is a precise "I picked this" hook, with `transform.position`, `m_itemPrefab.name`,
  `m_amount`, `m_respawnTimeMinutes` all in hand.
- `m_respawnTimeMinutes == 0` means one-shot (wild seeds, which then get `m_nview.Destroy()`).
  The value is on the prefab, so the mod never needs a hard-coded respawn table, and modded or
  Deep North pickables work automatically.
- ZDO keys `picked` / `picked_time` (DateTime ticks from `ZNet.GetTime()`) let any client compute
  a respawn ETA while the object is loaded. `picked_time` is written by the owner up to a minute
  late, so record local time at pick as well.
- `PickableItem` (loose stones/branches) is destroyed on pick and never respawns: ignore.
- Cultivated crops grow via `Plant.Grow()` into the same `Pickable` prefabs
  (`Pickable_Carrot` etc.). `EffectArea.IsPointInsideArea(pos, EffectArea.Type.PlayerBase)`
  (verified) tells you the pick happened inside a workbench radius: the cheap way to skip farms.

**Mining**
- `MineRock5`, `MineRock` and `Destructible` all implement `IDestructible.Damage(HitData)`, which
  runs on the **attacker's** client before forwarding to the owner. `hit.GetAttacker() ==
  Player.m_localPlayer` is the game's own test (used for mining stats). Re-check
  `hit.CheckToolTier(m_minToolTier)` in the prefix so a wooden-club bounce doesn't pin silver.
- Depletion: `MineRock5.DamageArea` returns true when a segment dies; `AllDestroyed()` →
  `m_nview.Destroy()`. Non-owners see `RPC_SetAreaHealth` and can decode the Base64 `s_health`
  ZPackage for a "% mined" figure. `Destructible.Destroy` fires the public `m_onDestroyed`
  (owner only); any client sees the object vanish via `ZNetScene`.
- Copper/silver deposits are a `Destructible` shell that spawns a `*_frac` `MineRock5`, re-applying
  the hit (so one pick swing can trigger two `Damage` calls: dedupe by position). Tin is
  `MineRock_Tin` (MineRock) per the wiki; the assembly can't confirm prefab composition, so
  classify at runtime by component + `DropTable` item names rather than by prefab list.
- No ore respawns. Flametal veins sink (1 %/hit chance to wake the Lavaiathan) so the object
  simply disappears: treat like depletion.

**Map pins**
- `Minimap.AddPin(Vector3, PinType, string, bool save, bool isChecked, long ownerID = 0,
  PlatformUserID author = default)` is public. **No pin limit in code.** `m_pins` and
  `GetClosestPin` are private. Custom sprites: set `pin.m_icon` after creating the pin; the
  marker copies it at creation and the save file stores only `(int)m_type`, so re-apply on
  load. `AddPin`'s `PlatformUserID` parameter drags in `Splatform.dll`; PortalLines builds the
  `PinData` by hand instead (`PortalLines/src/PortalLines/UI/PortalPins.cs`).
- Saved pins (`m_save == true`) go into the character's `.fch` per world UID **and** are exported
  by `GetSharedMapData` to the cartography table (everything except Death). The table has a
  known unfixed bug (since Ashlands, still reported Sept 2025) where deleted pins come back on
  the next Record/Read. **Auto-pins must not be `m_save` pins**, or they will flood every co-op
  map and be resurrected forever. Use `save:false` and persist them yourself.
- Names render only on the large map below zoom 0.5; icons are never zoom-hidden. Pin type
  filter (right-click icon row) is per `PinType`; `PinType.None` pins are outside it, so give
  the mod its own toggle (PortalLines honours the Icon4 filter by hand as a precedent).
- Jotunn 2.30.x `MinimapManager` has **no pin API**, only overlays and the events
  `OnVanillaMapAvailable` / `OnVanillaMapDataLoaded`. Jotunn is therefore optional for this mod.

**World / identity / time**
- `ZNet.instance.GetWorldUID()` keys the world; `Player.GetPlayerID()` the character.
  `Player.m_customData` (string→string) is saved with the character; PortalLines' TSV cache in
  `BepInEx/config/<Mod>/` keyed by `World.m_uid` is the alternative and survives character
  file corruption/rollback independently.
- `ZNet.instance.GetTimeSeconds()` is server-synced. Day = `EnvMan.instance.m_dayLengthSec`
  (1200 s). Respawn ETA = pickedTicks/1e7 + minutes×60 − now.

---

## 3. Resource reference

Wild pickables (world-gen placed, fixed positions per seed):

| Pickable | Biome | Respawn (real min) | Note |
|---|---|---|---|
| Raspberries / Blueberries / Cloudberries | Meadows / Black Forest / Plains | 300 | bush is Destructible; **never regrows if the bush is destroyed** |
| Mushroom (red), Thistle, Dandelion, Smoke puff | Meadows/BF/Swamp, BF/Swamp, Meadows, Ashlands | 240 | thistle spawns in groups of 1–5 |
| Yellow mushroom | dungeon interiors | 240 | only regenerating thing in a crypt |
| Blue mushroom, Jotun puffs, Magecap, Vineberry | Frost caves, Mistlands, Ashlands | wiki silent | read `m_respawnTimeMinutes` at runtime |
| Fiddlehead | Ashlands ruins | 300 | yields 3 |
| Carrot / Turnip seeds (wild) | BF / Swamp | **none** | one-shot; prefab destroyed on pick |
| Flax / Barley (wild) | Fuling villages | unverified | treat as one-shot |
| Deep North: Lingonberries, Kale, Oats, Poteitr, Luminous larva, Snowball | Deep North | **unverified** (wiki WIP) | runtime read |

Bog Witch added no wild pickables. Onion seeds are chest loot, not a ground pickable.

Mining nodes (all finite):

| Node | Component | Yield |
|---|---|---|
| Copper deposit (`rock4_copper` → `_frac`) | Destructible → MineRock5 | ~117 copper at 90 % mined |
| Tin (`MineRock_Tin`) | MineRock | 3–4 tin |
| Silver vein (`silvervein` → `_frac`) | Destructible → MineRock5, buried 4 m | ~83 silver |
| Obsidian (`MineRock_Obsidian`) | MineRock | small |
| Muddy scrap pile (`mudpile2`, `mudpile_beacon`) | MineRock5 | ~4–6 scrap |
| Giant remains (`giant_skull/ribs` `_frac`) | MineRock5 | ~390 black marble, soft tissue from skull |
| Flametal vein (on Lavaiathan) | MineRock5 | ~82, node sinks |
| Grausten, Deep North Frostcore / Ice | MineRock5 / unverified | unverified |

Vanilla auto-pins only locations (traders, vegvisir altars, events, Hildir quests); it never
shows any resource on the map.

---

## 4. Ecosystem state (Sept 2026)

- Valheim **1.0 released 2026-09-09** (Deep North, Unity 6000.0.75f1, new world save format).
  Current patch **1.0.15 (2026-09-18)**; the Recount `lib/` assembly is 1.0.14 (2026-09-17).
  Hook targets should be re-checked against 1.0.15 before first build.
- BepInExPack_Valheim 5.4.2350 (still BepInEx 5) loads on 1.0; mass breakage was from renamed
  game members, not the loader. Jotunn 2.30.0 → 2.30.2 (2026-09-21) ported for 1.0.
- Many pinner mods were re-released in the 2026-09-11 → 09-21 window; the field is being
  re-fought right now, and several have "broke after update" issues open. A clean 1.0-native
  mod has a timing advantage.

---

## 5. Design decisions to make before coding

1. **Trigger scope.** Interaction only (pick / first pickaxe hit by the local player). No radius
   scan, ever. This is the product.
2. **Clustering.** One pin per patch: merge same-item picks within ~15–20 m into a pin that
   carries a count ("Raspberries ×6"). Configurable radius. Without this the map dies (thistle
   groups, berry rows).
3. **Lifecycle.**
   - Respawning pickable: pin dims/crosses when every tracked bush in the cluster is picked,
     un-crosses when the earliest respawn is due (local timer; corrected from the ZDO when the
     area is loaded again). Optional "ready" indicator / tooltip with ETA.
   - One-shot pickable (`m_respawnTimeMinutes == 0`): don't pin, or pin as transient info only.
   - Ore: pin on first hit; remove when the object is destroyed or ≥ N % mined (config,
     AutoPin uses 90). Bushes destroyed by fire/troll: remove pin when the bush's ZDO vanishes.
4. **Storage and sharing.** Own store, keyed by world UID (+ character). Pins created with
   `save:false` so they never enter the `.fch` map data or the cartography table. Provide an
   explicit "promote to real pin" action for the ones a player wants to share.
5. **Farms.** Skip picks inside a `PlayerBase` effect area by default (config), plus a prefab
   exclusion list. Otherwise every carrot harvest pins the farm.
6. **Icons.** Item sprite (`ItemDrop.m_itemData.GetIcon()`) like The Greatest Map, or a small
   custom set. Must survive reload (re-apply `m_icon`). Own visibility toggle (hotkey / map
   button) since `PinType.None` is outside the vanilla filter.
7. **Right-click delete / click-to-check.** `save:false` pins are invisible to `GetClosestPin`,
   so the mod must handle its own clicks (PortalLines already documented this).
8. **Multiplayer stance.** Client-only, no server component, other players' picks not recorded.
   Say so in the README; it is a feature.
9. **Dependencies.** Jotunn adds nothing this mod needs. BepInEx-only widens compatibility, but
   the sibling projects' tooling assumes Jotunn; either is fine.
10. **Performance.** Pin count stays low by construction (clustering + interaction gate). Still:
    batch `m_pinUpdateRequired`, keep names short, consider hiding on the small minimap.

## 6. Brainstorm: things that would make it more than a pinner

- **Gather run**: sort ready patches by distance / by route, reuse PortalLines' route planner to
  draw a loop through portals. HUD arrow to the nearest ready patch.
- **Respawn clock** on hover: "ready in 1d 4h" in game days.
- **Yield memory**: remember how much each cluster gave last time so a patch label reads
  "Thistle ×5 (12 last run)".
- **Wishbone honesty**: silver/mud piles pin only on first hit, which already needs the Wishbone
  or luck; no special casing required.
- **Shareable export**: console command that writes the store to a text file, or promotes a
  selection to real pins for the cartography table.
- **Home suppression** beyond workbench radius: don't pin within N m of a bed/portal.
- **Stats**: harvests per item, best patch, ties into the sibling Tally mod's style.

## 7. Open questions (need a decision, not research)

- Pin on the first pick only, or keep updating the count every time?
- Ores in v1, or pickables first?
- Vanilla icons (five types, filterable) vs item sprites (prettier, needs own toggle)?
- Store in `Player.m_customData` (travels with the character file) or PortalLines-style TSV
  cache (survives character rollback, easy to inspect)?
- Keep the name Trove despite the FFXIV plugin of the same name?
