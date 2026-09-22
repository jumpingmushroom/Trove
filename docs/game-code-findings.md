# Trove — game-code findings

**Target build:** Valheim **1.0.14** (`Version.CurrentVersion = new GameVersion(1, 0, 14)`,
`Version.cs:168`). Source: `/workspace/gamemods/Valheim/Recount/lib/assembly_valheim.dll`
(dated 2026-09-17), decompiled with `ilspycmd` 9.1.0 (`~/.dotnet/tools/ilspycmd`, needs
`DOTNET_ROOT=$HOME/.dotnet DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`; note it is not on PATH in a
non-login shell — call it by absolute path). `Utils` lives in `assembly_utils.dll`, not
`assembly_valheim.dll`. Jotunn in the same folder is **2.30.1** (`Jotunn.Main.Version`).

Decompiled files are in `scratchpad/src/<Type>.cs`; line numbers below refer to those files.
Every statement marked **[verified]** is read from the decompiled code. **[assumption]** marks
prefab/asset-level facts that the DLL cannot show (they live in Unity asset bundles).

---

## 1. Pickable (berries, thistle, mushrooms, dandelion, cloudberries, flax, barley, …)

`public class Pickable : MonoBehaviour, Hoverable, Interactable` (`Pickable.cs:4`).

### 1.1 Fields [verified]

| Field | Line | Meaning |
|---|---|---|
| `GameObject m_hideWhenPicked` | 8 | visual child toggled off while picked; if null and no respawn, the object is destroyed on pick |
| `GameObject m_itemPrefab` | 10 | the item dropped (an `ItemDrop` prefab) |
| `int m_amount = 1` | 14 | base drop count |
| `int m_minAmountScaled`, `bool m_dontScale` | 16, 18 | resource-rate scaling controls |
| `DropTable m_extraDrops` | 20 | extra drops (seeds, etc.) |
| `string m_overrideName` | 22 | hover name override |
| `float m_respawnTimeMinutes` | 24 | **0 = never respawns** (one-shot pickable) |
| `float m_respawnTimeInitMin/Max` | 26, 28 | randomised initial "already picked long ago" offset (×100 min, see `UpdateRespawn`) |
| `EffectList m_pickEffector` | 36 | pick VFX/SFX |
| `bool m_useInteractAnimation` | 40 | return value of `Interact` |
| `bool m_tarPreventsPicking`, `float m_aggravateRange` | 42, 44 | tar check, and aggravate-nearby-AI on pick |
| `bool m_defaultPicked`, `bool m_defaultEnabled` | 46, 48 | ZDO defaults |
| `SpawnCheck m_spawnCheck` (delegate `bool SpawnCheck(Pickable p)`) | 6, 50 | optional respawn veto |
| `bool m_harvestable`, `Skills.SkillType m_pickRaiseSkill`, `float m_maxLevelBonusChance`, `int m_bonusYieldAmount`, `EffectList m_bonusEffect` | 52–60 | farming-skill bonus yield |
| `PlayerStatType m_harvestStat = PlayerStatType.None` | 62 | stat bumped on pick (`HarvestBerry`, `HarvestMushroom`, `HarvestCrop`, `HarvestVine`, … exist in `PlayerStatType`) |
| private `ZNetView m_nview; bool m_picked; bool m_pickedLocal; int m_enabled = 2; long m_pickedTime` | 64–74 | runtime state |
| `public int GetEnabled => m_enabled` | 76 | |

Public accessors: `GetHoverText()` (114), `GetHoverName()` (123 — `m_overrideName` else
`m_itemPrefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_name`, a `$item_…` token),
`GetPicked()` (295), `SetPicked(bool)` (269), `SetEnabled(bool|int)` (300/305),
`CanBePicked()` (318), `GetHoverOffset()` (351).

### 1.2 ZDO state [verified]

`Awake()` (`Pickable.cs:78-112`) registers two RPCs and reads the ZDO:

```csharp
m_nview.Register<bool>("RPC_SetPicked", RPC_SetPicked);
m_nview.Register<int>("RPC_Pick", RPC_Pick);
m_picked = zDO.GetBool(ZDOVars.s_picked, m_defaultPicked);
m_pickedTime = m_nview.GetZDO().GetLong(ZDOVars.s_pickedTime, 0L);
...
if (m_respawnTimeMinutes > 0f) InvokeRepeating("UpdateRespawn", Random.Range(1f, 5f), 60f);
```

ZDO keys (`ZDOVars.cs`): `s_picked = "picked"` (209), `s_pickedTime = "picked_time"` (9),
`s_enabled = "enabled"` (99). `picked_time` stores **`DateTime.Ticks`** derived from
`ZNet.instance.GetTime()` (see §5), not seconds.

Respawn: `UpdateRespawn()` (132–158) runs **only on the owner** (`!m_nview.IsOwner()` → return)
once a minute; `ShouldRespawn()` (160–178) compares
`ZNet.instance.GetTime() - new DateTime(pickedTimeTicks)` against `m_respawnTimeMinutes` and
then calls `m_nview.InvokeRPC(ZNetView.Everybody, "RPC_SetPicked", false)`.
So "respawns at" = `new DateTime(zdo.GetLong(ZDOVars.s_pickedTime)) + TimeSpan.FromMinutes(m_respawnTimeMinutes)`
in ZNet-time, readable by **any** client from the ZDO (the ZDO is synced; only the *write* is
owner-only).

### 1.3 Interaction path [verified]

1. `Player.Update()` (`Player.cs:853`) returns early unless `m_nview.IsValid() && m_nview.IsOwner()`
   (871), then on the Use key calls `Interact(m_hovering, hold, alt)` (905/914) →
   `Player.Interact(GameObject go, bool hold, bool alt)` (4307) →
   `go.GetComponentInParent<Interactable>().Interact(this, hold, alt)` (4313–4317).
   **`Pickable.Interact` therefore executes only on the picking player's own client.**
2. `Pickable.Interact(Humanoid character, bool repeat, bool alt)` (180–228):
   - bails if `!m_nview.IsValid() || m_enabled == 0`, or stuck in tar;
   - if `!m_picked && character is Player player`: on the first local pick (`!m_pickedLocal`)
     increments `m_harvestStat` via `Game.instance.IncrementPlayerStat` and
     `Game.instance.GetPlayerProfile().IncrementStatPickable(m_itemPrefab.name)` (203–208), rolls
     the skill bonus, sets `m_pickedLocal = true`;
   - **always** ends with `m_nview.InvokeRPC("RPC_Pick", num)` (226) — this is the owner-targeted
     overload (`ZNetView.InvokeRPC(string, params)` → `InvokeRoutedRPC(m_zdo.GetOwner(), …)`,
     `ZNetView.cs:331-334`), which `ZRoutedRpc.InvokeRoutedRPC` handles synchronously when the
     owner is this peer and otherwise sends over the wire (`ZRoutedRpc.cs:130-137`);
   - returns `m_useInteractAnimation`.
   Note `Interact` does **not** check `m_picked` before sending the RPC; spam-pressing sends
   RPC_Pick repeatedly, and `RPC_Pick` de-duplicates via `m_picked`.
3. `RPC_Pick(long sender, int bonus)` (235–262) runs **only on the owner**
   (`if (!m_nview.IsOwner() || m_picked) return;`). It spawns `m_pickEffector`, computes
   `num = m_dontScale ? m_amount : max(m_minAmountScaled, Game.instance.ScaleDrops(m_itemPrefab, m_amount))` + bonus,
   calls `Drop(m_itemPrefab, i, 1)` `num` times (one ItemDrop instance per unit), then the
   `m_extraDrops` table, optional `BaseAI.AggravateAllInArea`, and finally
   `m_nview.InvokeRPC(ZNetView.Everybody, "RPC_SetPicked", true)` (261).
   Caveat: it uses `Player.m_localPlayer.GetZDOID()` for the effect's exclusive-player id (242),
   i.e. the *owner's* local player, not the picker — the picker identity is **not** carried in the
   RPC beyond `sender` (the peer id of the picking client).
4. `RPC_SetPicked(long sender, bool picked)` → `SetPicked(bool)` (264–293) runs on **every**
   client: sets `m_picked`, toggles `m_hideWhenPicked`; then, **owner only**, writes
   `s_picked` and (if `m_respawnTimeMinutes > 0`) `s_pickedTime = ZNet.instance.GetTime().Ticks`
   (282–287); if there is no respawn and no hide-object it `m_nview.Destroy()`s (291).

**Hook recommendation.** A Harmony postfix on `Pickable.Interact` (or prefix capturing
`m_picked`/`m_enabled` before the call) fires exactly once per press on the local client with
`this.transform.position`, `this.m_itemPrefab.name`, `this.m_respawnTimeMinutes`, and
`character == Player.m_localPlayer`. To pin only *successful* picks, check
`!__instance.GetPicked() && __instance.GetEnabled == 1` in a prefix and remember it for the
postfix; or postfix `SetPicked(true)` (fires everywhere, so filter on a "I just interacted with
this instance" flag set in the `Interact` prefix). `m_pickedLocal` is private and never reset,
so it is only a "this client has picked this instance at least once since load" flag.

### 1.4 Prefab name [verified]

There is **no static registry of Pickables**. Prefab name of a placed instance:
`Utils.GetPrefabName(gameObject)` (`Utils.cs:200-213`, strips `(Clone)`/suffix chars), which is
what `ZNetView.GetPrefabName()` (private, `ZNetView.cs:217-220`) uses. From the ZDO:
`zdo.GetPrefab()` (`ZDO.cs:563`) returns the stable hash; `ZNetScene.instance.GetPrefab(int hash)`
(`ZNetScene.cs:137`) and `GetPrefab(string)` (146) resolve it; `ZNetScene.m_prefabs`
(`List<GameObject>`, line 13) / `GetPrefabNames()` (437) enumerate all registered prefabs, so
`ZNetScene.instance.m_prefabs.Where(p => p.GetComponent<Pickable>())` yields the full pickable
list at runtime. The *item* name is `m_itemPrefab.name` (e.g. `Raspberry`) and its display token
`m_itemPrefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_name` (e.g. `$item_raspberries`).

### 1.5 PickableItem (loose stone/wood/flint on the ground) [verified]

`public class PickableItem : MonoBehaviour, Hoverable, Interactable` (`PickableItem.cs:4`).
Fields: `ItemDrop m_itemPrefab` (16), `int m_stack` (18), `RandomItem[] m_randomItemPrefabs`
(20, struct `{ItemDrop m_itemPrefab; int m_stackMin; int m_stackMax}`), `EffectList m_pickEffector`
(22). ZDO keys `s_itemPrefab = "itemPrefab"`, `s_itemStack = "itemStack"` (`ZDOVars.cs:147,149`)
only for the random variant. RPC name is `"Pick"` (38). `Interact` (98–106) just
`m_nview.InvokeRPC("Pick")` and returns true; `RPC_Pick(long sender)` (113–122) on the owner
drops once and **`m_nview.Destroy()`s the object — PickableItems never respawn.** They are not
worth pinning (one-shot, random scatter).

---

## 2. Mining

All three implement `IDestructible { void Damage(HitData hit); DestructibleType GetDestructibleType(); }`
(`IDestructible.cs`). `Damage()` runs on the **attacker's** client (called from `Attack`/`Projectile`
hit resolution) and forwards to an **owner-only** RPC. `HitData.m_attacker` is a `ZDOID`
(`HitData.cs:700`), set by `Attack.cs:970/1135/1410` and `Projectile.cs:519/652` via
`hitData.SetAttacker(m_character)`; `HitData.GetAttacker()` (1053–1081) resolves it through
`ZNetScene.instance.FindInstance(m_attacker)`. The game's own "was it me" test is
`hit.GetAttacker() == Player.m_localPlayer` — used by all three types below for stats, so it is
reliable **on the owner**. On a non-owner client (e.g. you hit a rock owned by another peer)
`RPC_Damage` returns immediately, so the only local-side signal is `Damage()` itself.

Tool tier: `HitData.CheckToolTier(int minToolTier, bool alwaysAllowTierZero = false)`
(`HitData.cs:1083-1094`) compares `m_toolTier` (short, 686) and the world-level lock.
`HitData.HitType` enum (`HitData.cs:80-107`): `Undefined, EnemyHit, PlayerHit, Fall, Drowning,
Burning, Freezing, Poisoned, Water, Smoke, EdgeOfWorld, Impact, Cart, Tree, Self, Structural,
Turret, Boat, Stalagtite, Catapult, CinderFire, AshlandsOcean, AshlandsLava, Incinerator,
DrawBridge`.

### 2.1 MineRock (older multi-part rock) [verified]

`public class MineRock : MonoBehaviour, IDestructible, Hoverable` (`MineRock.cs:4`).
Fields: `string m_name` (6), `float m_health = 2f` (8), `bool m_removeWhenDestroyed = true` (10),
`HitData.DamageModifiers m_damageModifiers` (12), `int m_minToolTier` (14), `GameObject m_areaRoot`,
`m_baseModel` (16, 18), `EffectList m_destroyedEffect`, `m_hitEffect` (20, 22),
**`DropTable m_dropItems`** (24), `Action m_onHit` (26, public — a free hook, fires on every
damaging hit on the owner), private `Collider[] m_hitAreas` (30).

- RPCs registered in `Start()` (36–54): `"Hit"` `(HitData, int)` and `"Hide"` `(int)`.
- `Damage(HitData hit)` (103–118): needs `hit.m_hitCollider`, maps it to an area index, then
  `m_nview.InvokeRPC("Hit", hit, areaIndex)` (owner-targeted).
- `RPC_Hit(long sender, HitData hit, int hitAreaIndex)` (120–219): owner-only; per-area health is
  stored in the ZDO under **string keys `"Health" + index`** (132–133, `GetFloat(text, GetHealth())`);
  applies resistance + tool tier; on area death: `m_destroyedEffect`, `InvokeRPC(Everybody, "Hide", idx)`
  (181), instantiates every prefab from `m_dropItems.GetDropList()` (182–186), and
  **`if (m_removeWhenDestroyed && AllDestroyed()) m_nview.Destroy();`** (187–190).
  Stats `MineHits`, `Mines`, `MineTier0..5` are bumped when `hit.GetAttacker() == Player.m_localPlayer`.
- `AllDestroyed()` (221–232) is private: every `"Health"+i` ≤ 0. `GetHealth()` (277) =
  `m_health * (1 + worldLevel * m_worldLevelMineHPMultiplier)`.

### 2.2 MineRock5 (copper/silver/tin-in-Ashlands style multi-piece deposits) [verified]

`public class MineRock5 : MonoBehaviour, IDestructible, Hoverable` (`MineRock5.cs:5`).
Fields: `string m_name` (43), `float m_health = 2f` (45), `HitData.DamageModifiers m_damageModifiers`
(47), `int m_minToolTier` (49), `bool m_supportCheck = true` (51), `bool m_triggerPrivateArea` (53),
`EffectList m_destroyedEffect`, `m_hitEffect` (55, 57), **`DropTable m_dropItems`** (59),
`bool m_hitEffectAreaCenter` (61); private `List<HitArea> m_hitAreas` (65, `HitArea` is a private
class with `Collider m_collider; float m_health; bool m_supported; …` (16–33)),
`bool m_allDestroyed` (81), `uint m_lastDataRevision` (77).

- RPCs registered in `Awake()` (133–138): `"RPC_Damage"` `(HitData, int)` and
  `"RPC_SetAreaHealth"` `(int, float)`.
- **Health storage:** one ZDO string under `ZDOVars.s_health` (`"health"`, `ZDOVars.cs:119`)
  holding a Base64 `ZPackage` of `int count` then `count` floats (`LoadHealth` 174–192,
  `SaveHealth` 194–205). A non-owner client can decode this itself to know depletion.
  `CheckForUpdate()` (165–172) re-reads it every 10 s when `ZDO.DataRevision` changed.
- `Damage(HitData hit)` (286–336): if `hit.m_hitCollider == null || hit.m_radius > 0` it
  overlap-spheres for areas and sends one `"RPC_Damage"` per area, else maps `m_hitCollider` to an
  index and sends one `InvokeRPC("RPC_Damage", hit, idx)` (owner-targeted).
- `RPC_Damage(long sender, HitData hit, int idx)` (338–357): owner-only → `DamageArea(idx, hit)`;
  if that destroyed an area and `m_supportCheck`, `CheckSupport()` (143–163) knocks out unsupported
  pieces with a synthetic `HitType.Structural` hit (no attacker).
- `DamageArea(int, HitData)` (359–449): resistance, tool tier, `hitArea.m_health -= totalDamage;
  SaveHealth();` (387–388), effects, `MineHits` stat if `hit.GetAttacker() == Player.m_localPlayer`
  (400). On area death: `InvokeRPC(Everybody, "RPC_SetAreaHealth", idx, health)` (406),
  `m_destroyedEffect`, drops from `m_dropItems.GetDropList()` (408–412), and
  **`if (AllDestroyed()) { m_nview.Destroy(); m_allDestroyed = true; }`** (413–417), then
  `Mines`/`MineTier*` stats (418–445). Returns true only when an area died.
- `AllDestroyed()` (451–461) and `NonDestroyed()` (463–473) are **private**; use a Harmony
  reverse patch / `AccessTools`, or decode `s_health` yourself.
- `RPC_SetAreaHealth(long sender, int index, float health)` (475–483) runs on everyone and is the
  non-owner's per-piece signal (sender = owner peer id, not the miner).

### 2.3 Destructible (tin nodes, obsidian, guck, ore "surface" rocks, most one-piece nodes) [verified]

`public class Destructible : MonoBehaviour, IDestructible` (`Destructible.cs:5`).
Fields: `Action m_onDestroyed`, `Action m_onDamaged` (7, 9 — **public delegates**),
`DestructibleType m_destructibleType` (12; enum `None=0, Default=1, Tree=2, Character=4, Everything=7`),
`float m_health = 1f` (14), `HitData.DamageModifiers m_damages` (16), `float m_minDamageTreshold`
(18), `int m_minToolTier` (20), `float m_hitNoise`, `m_destroyNoise` (22, 24),
`bool m_triggerPrivateArea` (26), `float m_ttl` (28), `GameObject m_spawnWhenDamaged`,
**`GameObject m_spawnWhenDestroyed`** (30, 32), `EffectList m_destroyedEffect`, `m_hitEffect`,
`m_hitEffectBig`, `m_hitEffectBuildup` (35–47), `bool m_autoCreateFragments` (39).

- Health is a single float in the ZDO under `ZDOVars.s_health` (`"health"`), default
  `m_health * (1 + worldLevel * mult)` (106).
- `Damage(HitData)` (92–98): `m_nview.InvokeRPC("RPC_Damage", hit)` (owner-targeted).
- `RPC_Damage(long sender, HitData hit)` (100–171): owner-only; resistance, tool tier, writes
  health, effects, `m_onDamaged()`, `m_spawnWhenDamaged`, and **`if (num <= 0f) Destroy(hit);`**.
- `Destroy(HitData hit = null)` (181–228): effects, cheat flag `s_cheated`, then
  `Instantiate(m_spawnWhenDestroyed, position, rotation)` and — important for ore —
  **`gameObject.GetComponent<MineRock5>()?.Damage(hit);`** (219): the replacement MineRock5 gets
  the same hit re-applied. Then `m_onDestroyed()`, `ZNetScene.instance.Destroy(gameObject)`,
  `m_destroyed = true`.
- **Drops** are not on Destructible itself: `DropOnDestroyed` (`DropOnDestroyed.cs:5`) has
  `DropTable m_dropWhenDestroyed` (8) and subscribes to `Destructible.m_onDestroyed` /
  `WearNTear.m_onDestroyed` in `Awake()` (14–27); `OnDestroyed()` (29–46) instantiates each prefab
  from `m_dropWhenDestroyed.GetDropList()` at the object's position. It reads `s_cheated` from the
  ZDO, so it only runs on the owner (it is called from `Destroy` which only the owner reaches).

**[assumption, not provable from the DLL]:** which prefab uses which component. Community
knowledge: copper/silver/"rock4_copper"-style deposits are a `Destructible` whose
`m_spawnWhenDestroyed` is the `*_frac` `MineRock5`, and the frac is what you actually mine;
tin, obsidian, guck, flametal and giant remains are `Destructible` + `DropOnDestroyed`; the
legacy `MineRock` is rare. Verify by listing `ZNetScene.instance.m_prefabs` at runtime and
logging which of `Pickable` / `Destructible` / `MineRock` / `MineRock5` / `DropOnDestroyed` each
carries, then pick the ones whose drop table contains an ore item.

### 2.4 DropTable [verified]

`[Serializable] public class DropTable` (`DropTable.cs:6`): `List<DropData> m_drops` (28;
`DropData { GameObject m_item; int m_stackMin; int m_stackMax; float m_weight; bool m_dontScale; }`
9–20), `int m_dropMin = 1`, `m_dropMax = 1` (30, 32), `float m_dropChance = 1f` (35),
`bool m_oneOfEach` (37). `GetDropList()` (103) returns prefabs (one entry per unit),
`GetDropListItems()` (44) returns `ItemDrop.ItemData` with `m_dropPrefab`/`m_stack`; `IsEmpty()`
(179). To classify a node as "ore", inspect `m_dropItems.m_drops[i].m_item.name` (e.g.
`CopperOre`, `TinOre`, `SilverOre`) — a static read, no RNG.

### 2.5 Local-player attribution and hook points [verified]

- The one signal that always runs on the hitting client is `X.Damage(HitData)`; a prefix there
  with `hit.GetAttacker() == Player.m_localPlayer` (or `hit.m_attacker == Player.m_localPlayer.GetZDOID()`)
  identifies "I hit this node". Position: `__instance.transform.position` (node root) or
  `hit.m_point`; name: `__instance.m_name` (MineRock/MineRock5, already a `$…` token) or
  `Utils.GetPrefabName(__instance.gameObject)`.
- Depletion, owner side: postfix `MineRock5.DamageArea` (bool result true = an area died) and then
  read `__instance.m_allDestroyed` (private) or `AllDestroyed()`; or postfix `MineRock5.RPC_Damage`;
  `Destructible.Destroy`; `MineRock.RPC_Hit`.
- Depletion, any side: `MineRock5.RPC_SetAreaHealth` postfix + decode `s_health`; or watch
  `ZNetScene.Destroy` for a tracked ZDOID. For Destructible, `m_onDestroyed` fires only on the
  owner.
- A hit that does not meet `m_minToolTier` still reaches `Damage()` — `CheckToolTier` is inside the
  owner RPC. To avoid pinning "too hard" hits, repeat `hit.CheckToolTier(__instance.m_minToolTier)`
  in the prefix (Recount PLAN §1.3 does exactly this).

---

## 3. Minimap pins [verified]

`Minimap.instance` (`Minimap.cs:410`, `static Minimap instance => s_instance`).

### 3.1 Types

```csharp
public enum PinType { Icon0, Icon1, Icon2, Icon3, Death, Bed, Icon4, Shout, None, Boss, Player,
                      RandomEvent, Ping, EventArea, Hildir1, Hildir2, Hildir3, Memorial }   // 25-45
public class PinData {                                                                  // 47-80
    public string m_name; public PinType m_type; public Sprite m_icon; public Vector3 m_pos;
    public bool m_save; public long m_ownerID; public PlatformUserID m_author;
    public bool m_shouldDelete; public bool m_checked; public bool m_doubleSize; public bool m_animate;
    public float m_worldSize; public RectTransform m_uiElement; public GameObject m_checkedElement;
    public Image m_iconElement; public PinNameData m_NamePinData; }
[Serializable] public struct SpriteData { public PinType m_name; public Sprite m_icon; }   // 123-128
public List<SpriteData> m_icons;                                                        // 260
```

**[assumption]** Icon0 = fire/campfire, Icon1 = house, Icon2 = hammer (the "mine" icon
players use for ore), Icon3 = dot, Icon4 = portal; the DLL only shows the `m_selectedIcon0..4`
UI fields, the sprites are asset data.

### 3.2 API

- `public PinData AddPin(Vector3 pos, PinType type, string name, bool save, bool isChecked, long ownerID = 0L, PlatformUserID author = default)`
  (2352–2385). Invalid type → `Icon3`; null name → `""`; `m_icon = GetSprite(type)` (2369);
  a `PinNameData` is created only when the name is non-empty (2374–2377); adds to `m_pins`;
  if that type is currently filtered off it **toggles the filter back on** (2379–2382);
  sets `m_pinUpdateRequired = true`. **No pin limit anywhere** (`m_pins.Count` is only used for
  save/iteration: lines 624, 2164, 2815, 2849).
- `public bool DiscoverLocation(Vector3 pos, PinType type, string name, bool showMap)` (2309–2338):
  the vanilla "add a saved pin with a HUD message" helper; dedupes with
  `HaveSimilarPin(pos, type, name, save: true)` (2340–2350: same name, type, save flag, and
  `Utils.DistanceXZ < 1f`), shows `$msg_pin_added: name` with the sprite.
- `public void RemovePin(PinData pin)` (2292–2297) and `public bool RemovePin(Vector3 pos, float radius)`
  (2245–2254) → **private** `GetClosestPin(Vector3 pos, float radius, bool mustBeVisible = true)`
  (2273–2290; only considers `m_save == true` pins, XZ distance). Right-click on the large map
  calls `RemovePinUnderPointer()` (2528–2539) with `PinInteractRadius` — **so the user can delete
  our pins with the normal right-click**, which is desirable.
- `m_pins` is **private** `List<PinData>` (316); reach it via `AccessTools.FieldRefAccess`.
- `public void SaveMapData()` (2145–2148) / private `LoadMapData()` (2136–2143).
- `public byte[] GetSharedMapData(byte[] oldMapData)` (2707–2759) /
  `public bool AddSharedMapData(byte[] dataArray)` (2781–2861) — cartography table.
- `public void ShowPointOnMap(Vector3)` (2299), `public void OnToggleSharedMapData()` (2642).

### 3.3 Rendering cost and filtering

`UpdatePins()` (1627–1713) runs from `Update` only when `m_pinUpdateRequired` (760–764) — it is
**not** per frame unless something sets the flag (mode switch, zoom/pan: 1124, 1167, 1227, 1294,
1540–1616, add/remove, filter, shared-fade). Per pin it:
- destroys the marker (`DestroyPinMarker`, 1715–1726) when the pin is off-screen
  (`IsPointVisible`, 1798–1806, pure uvRect test), its type is filtered off
  (`m_visibleIconTypes[(int)pin.m_type]`), or it is a shared pin (`m_ownerID != 0`) while the
  shared-data fade is 0 (1648–1652);
- otherwise instantiates `m_pinPrefab` once (1653–1664) and sets `pin.m_iconElement.sprite = pin.m_icon`
  **only at creation** (1658), then repositions it every update;
- shared pins (`m_ownerID != 0`) are tinted `(0.7,0.7,0.7,0.8*fade)` (1641, 1673);
- **names are shown only on the large map and only when `LargeZoom < m_showNamesZoom`**
  (`m_showNamesZoom = 0.5f`, line 244; check at 1704–1711). Icons themselves are never hidden by
  zoom; there is no `m_showPinsBelowZoom`. Pin size is `m_pinSizeSmall = 32f` / `m_pinSizeLarge = 48f`
  (252, 254), doubled by `m_doubleSize`, or world-scaled by `m_worldSize > 0` (1693–1699).
- `m_animate` pulses the icon (1686–1692). `m_checked` toggles the "Checked" child (1700–1703).
  There is no `m_pinNameFont`; names are `TMP_Text` in `PinNameData` (86, 103) and pass through
  `CensorShittyWords.FilterUGC` when the author is another platform user (104–111).

**Custom icon per pin:** `PinData.m_icon` is a plain public `Sprite` and is copied into the
`Image` when the marker is created, so `var p = AddPin(...); p.m_icon = mySprite;` before the
next `UpdatePins` (which `AddPin` already scheduled) shows a custom sprite. The marker is only
rebuilt when it was destroyed (off-screen / mode switch), so changing `m_icon` later requires
`RemovePin`+re-add or setting `p.m_iconElement.sprite` directly if non-null. The saved format
stores only `(int)m_type` (2172), so a custom sprite must be re-applied after load
(`SetMapData` → `AddPin`, 2226–2235) — e.g. by name prefix or by keeping your own list.

### 3.4 Persistence

`GetMapData()` (2150–2183) writes version `8` (`Version.Map.PinsAuthor`; enum at
`Version.cs:120-129`: `Pins=2, PinsChecked, VisibleOnMap, NewExplore, PinsOwnerID, Compressed, PinsAuthor`),
then a compressed package: `m_textureSize`, explored bits, explored-others bits, and **only pins
with `m_save == true`**: `m_name, m_pos, (int)m_type, m_checked, m_ownerID, m_author.ToString()`
(2164–2177). `SetMapData` (2185–2243) throws on texture-size mismatch and rebuilds pins via
`AddPin(..., save: true, ...)`.

Storage location: `Minimap.SaveMapData()` → `Game.instance.GetPlayerProfile().SetMapData(bytes)`
(`PlayerProfile.cs:671-678`) → `GetWorldData(ZNet.instance.GetWorldUID()).m_mapData`.
`PlayerProfile.m_worldData` is `Dictionary<long, WorldPlayerData>` (100) keyed by **world UID**;
`WorldPlayerData` (12–29, private) holds spawn/logout/death/home points and `byte[] m_mapData`.
It is written inside the `.fch` character file (`PlayerProfile.Save`, 322–337). **So vanilla
pins are per character × per world.** `Game.SavePlayerProfile(bool setLogoutPoint, …)`
(`Game.cs:426`) calls `m_playerProfile.SavePlayerData(Player.m_localPlayer)` then
`Minimap.instance.SaveMapData()` (453–454).

### 3.5 Shared pins (cartography table)

`GetSharedMapData` (2734–2757) exports every pin with `m_save && m_type != PinType.Death`,
stamping `m_ownerID = (pin.m_ownerID != 0 ? pin.m_ownerID : Player.m_localPlayer.GetPlayerID())`
and the platform author. `AddSharedMapData` (2811–2859): marks existing foreign pins
(`m_ownerID != 0 && != myPlayerID`) for deletion, then for each incoming pin: if any saved pin is
within 1 m (`HavePinInRange`, 2256–2266) it is kept, else if the owner is not me it is added with
`save: true` and that `ownerID`; foreign pins no longer on the table are removed. **Every
saved pin type except Death is shared**, including any Trove pin added with `save: true`.
If you do not want your pins to spread to other players, use `save: false` (then they are not
persisted by the game either — you must persist and re-add them yourself), or filter them out
with a prefix on `GetSharedMapData` that temporarily flips `m_save`.

### 3.6 Jotunn 2.30.1

`Jotunn.Managers.MinimapManager` (`JotunnMinimapManager.cs`) offers **no pin API at all** — no
`AddPin`/`PinData`/`PinType` references. It provides texture overlays only:
`GetMapOverlay(string name, bool ignoreFog = false)` (427), `GetMapDrawing(string name)` (450),
`RemoveMapOverlay/RemoveMapDrawing`, `GetOverlayNames/GetDrawingNames`,
`WorldToOverlayCoords(Vector3, int texSize)` (502) / `OverlayToWorldCoords` (508), and the
events `OnVanillaMapAvailable`, `OnVanillaMapDataLoaded` (392–394). The latter fires after the
vanilla map data (and thus the saved pins) is loaded, which is a convenient point to re-add
custom pins. Use vanilla `Minimap.AddPin` directly; Jotunn is optional here.

---

## 4. Player identity / world [verified]

- Local player: `public static Player m_localPlayer` (`Player.cs:158`).
- Character id: `Player.GetPlayerID()` (752–759) reads `ZDOVars.s_playerID` from the player ZDO;
  the same number is `PlayerProfile.GetPlayerID()` (751) / `m_playerID`. Profile name:
  `PlayerProfile.GetName()` (746); file name: `PlayerProfile.m_filename` (92, public readonly).
- World: `ZNet.instance.GetWorldName()` (`ZNet.cs:2175-2182`, `m_world.m_name`, null before a
  world is set), `ZNet.instance.GetWorldUID()` (2170–2173, `m_world.m_uid`),
  `ZNet.instance.GetWorld()` (2184). `World.m_uid = name.GetStableHashCode() + Utils.GenerateUID()`
  at creation (`World.cs:74`), persisted in the world file (309). Seed:
  `WorldGenerator.instance.GetSeed()` (`WorldGenerator.cs:1445-1448`, `m_world.m_seed`) and
  `World.m_seedName` (26).
- Per-character custom data: `public Dictionary<string, string> Player.m_customData` (`Player.cs:581`),
  written in `Player.Save` (4748–4753) and read in `Player.Load` (4968–4974), both inside the
  profile's `m_playerData` blob (`PlayerProfile.SavePlayerData/LoadPlayerData`, 192–206). It is
  saved on every `Game.SavePlayerProfile`. Keys are free-form strings; this is the standard
  mod-persistence slot **per character (not per world)** — include the world UID in your key
  or value if the data is world-specific.
- Where to store Trove data: options, in order of fit —
  1. vanilla pins with `save: true` — persisted per character × world by the game itself, but
     shared via cartography tables and indistinguishable from user pins after reload (type only);
  2. `Player.m_customData["Trove." + worldUID]` — a JSON/ZPackage blob, per character,
     travels with the `.fch` file, no world dependency; size is fine for a few hundred entries;
  3. a mod-owned file under `BepInEx/config` keyed by `GetWorldUID()` + `GetPlayerID()`.

---

## 5. Timing [verified]

- `ZNet.instance.GetTimeSeconds()` → `double m_netTime` (`ZNet.cs:2938-2941`; server increments
  it in `UpdateNetTime` only while players are connected, 1360–1373; clients receive it via the
  `"NetTime"` RPC, 1221/1691/1696–1699). `ZNet.instance.GetTime()` (2927–2931) returns
  `new DateTime((long)(m_netTime * 1000.0 * 10000.0))` — i.e. `m_netTime` seconds as
  `DateTime.Ticks`; this is what Pickable stores in `picked_time`.
- Day length: `EnvMan.instance.m_dayLengthSec` (`EnvMan.cs:53`, `long`, default 1200 = 20 min).
  `EnvMan.instance.GetDay()` = `(int)(GetTimeSeconds() / m_dayLengthSec)` (944–952);
  `GetDayFraction()` (939) is a smoothed fraction; `GetMorningStartSec(int day)` (954).
- "Respawns in" for a pickable:
  ```csharp
  long t = zdo.GetLong(ZDOVars.s_pickedTime, 0L);             // ticks
  double pickedSec = t / 10_000_000.0;
  double readySec  = pickedSec + p.m_respawnTimeMinutes * 60.0;
  double remaining = readySec - ZNet.instance.GetTimeSeconds(); // game seconds
  ```
  (`ShouldRespawn` uses `TimeSpan.TotalMinutes <= m_respawnTimeMinutes` on the same numbers,
  160–178.) Game seconds → in-game days: `/ m_dayLengthSec`. Note `picked_time` is only written
  by the **owner** in `SetPicked` (285–287) or lazily in `UpdateRespawn` (138–146) up to a minute
  later, so immediately after your own pick on a non-owned pickable the ZDO may still read 0;
  compute the ETA from `GetTimeSeconds()` at pick time instead of waiting for the ZDO.
- Mining nodes have no respawn at all: `MineRock5`/`MineRock`/`Destructible` all `Destroy` the
  ZDO when depleted; nothing in these classes re-creates them.

---

## 6. Existing hooks that make this easy [verified]

- **`Pickable.Interact`** is the single, local-only choke point for gathering (§1.3). No
  `Player.m_lastPickedItem` exists (grep of `Player.cs` finds nothing of the kind).
- **Stats:** `Pickable.Interact` calls `Game.instance.IncrementPlayerStat(m_harvestStat)`
  (`Game.cs:836` → `PlayerProfile.IncrementStat`) and
  `PlayerProfile.IncrementStatPickable(string name, float amount = 1f, bool cheated = false)`
  (`PlayerProfile.cs:862-871`), which increments `m_playerStats[0].m_pickableStats[name]`
  (`Dictionary<string,float>`, line 47) keyed by **item prefab name**. `PlayerStatType` has
  `ItemsPickedUp`, `HarvestCrop`, `HarvestBerry`, `HarvestMushroom`, `HarvestVine`, `HarvestBonus`,
  `MineHits`, `Mines`, `MineTier0..5`. A postfix on `IncrementStatPickable` is an alternative
  pickable hook but carries no position; `Pickable.Interact` is better.
- **Mining stats:** `Mines`/`MineTier*` are incremented in `MineRock.RPC_Hit`,
  `MineRock5.DamageArea` only when `hit.GetAttacker() == Player.m_localPlayer` **and on the
  owner**, so a `Game.IncrementPlayerStat` postfix would miss nodes owned by other peers.
- **`Destructible.m_onDestroyed` / `m_onDamaged`** and **`MineRock.m_onHit`** are public
  `Action`s you can subscribe to per instance (owner-side only).
- `Pickable.m_pickEffector` / `MineRock5.m_destroyedEffect` are `EffectList`s
  (`EffectList.Create(Vector3, Quaternion, Transform, float, int, ZDOID)`, `EffectList.cs:37`);
  they are spawned on the owner, not useful as a local hook.
- **`Minimap.DiscoverLocation`** already implements "add a saved pin, dedupe within 1 m, show a
  `$msg_pin_added` toast with the icon" — usable as-is for a first version.

---

## 7. Summary of the recommended hook set

| Event | Hook | Runs on | Position / name |
|---|---|---|---|
| Gather pickable | `Pickable.Interact` prefix/postfix, guard `!GetPicked() && GetEnabled == 1 && character == Player.m_localPlayer` | picking client only | `transform.position`, `m_itemPrefab.name`, `GetHoverName()`, `m_respawnTimeMinutes` |
| Hit an ore node | `MineRock5.Damage` / `Destructible.Damage` / `MineRock.Damage` prefix, guard `hit.GetAttacker() == Player.m_localPlayer` and `hit.CheckToolTier(m_minToolTier)` | hitting client | `transform.position`, `m_name` (MineRock*), drop table item names |
| Node depleted (owner) | `MineRock5.DamageArea` postfix (result true → check `AllDestroyed()`), `Destructible.Destroy` prefix, `MineRock.RPC_Hit` postfix | owner only | |
| Node depleted (anyone) | `MineRock5.RPC_SetAreaHealth` postfix + decode `s_health`; or `ZNetScene.Destroy` on a remembered ZDOID | every client | |
| Pin | `Minimap.instance.AddPin(pos, PinType.Icon2, name, save, false)` (+ optional `m_icon`) | local | |
| Persist | vanilla `m_save`, or `Player.m_customData` keyed with `ZNet.instance.GetWorldUID()` | | |
| Re-add after load | Jotunn `MinimapManager.OnVanillaMapDataLoaded`, or postfix `Minimap.LoadMapData` | | |
