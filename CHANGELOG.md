# Changelog

## 0.1.2 — 2026-10-05

- **Pins now show on the large map only, unless you turn on `Pins.ShowOnMinimap`.** The setting
  existed in 0.1.x but did nothing, so pins always showed on the minimap too. If you want them
  there, set `Pins.ShowOnMinimap = true` in `BepInEx/config/com.jumpingmushroom.trove.cfg` or
  through the configuration manager.
- Dying no longer duplicates every pin. Each death left the old set on the map and added a new
  one; the stray copies are gone after a relog.
- A remembered plant or node could lose track of itself after a reload: its position was saved
  too coarsely and could round to a neighbouring spot. Positions are now saved exactly.
- One damaged line in the cache file no longer discards the whole file, and a file that cannot be
  read is left untouched instead of being overwritten.
- With `Pins.HideChecked` on, hidden patches no longer show a hover panel or take clicks.
- Crossed-out pins refresh on time again after switching worlds within one session.

## 0.1.1 — 2026-09-22

No code changes. The screenshots were missing from the Thunderstore package page; the
README now links them absolutely so they render there.

## 0.1.0 — 2026-09-22

First release. Published as Trove; the mod was prototyped under the working name GatherBuddy.


- Pickables: a pin with the item's icon and a count for every patch you pick from, clustered
  within 20 m. Crossed out while everything in the patch is picked, back when it has regrown.
- Ore: a pin on the first pickaxe hit on a deposit, removed when the node is gone.
- Per-world cache in `BepInEx/config/Trove/`. Pins are never saved into the character
  file and never shared through the cartography table.
- Hover a patch on the large map for what is in it, how much is ready and when the rest grows
  back. Left-click crosses it out by hand, right-click forgets it for good.
- Ore pins clear at 90% mined (configurable), not only when the last piece is gone: copper and
  silver keep pieces below the dig limit.
- Branches, snow branches and surface flint are excluded by default; they respawn but pin-per-stick
  is map clutter. Configurable under `Gather.ExcludedItems`.
- Plain destructibles count as nodes only when they are pickaxe-only, so stumps, bushes, barrels
  and furniture never pin. Grausten is ignored like stone.
- A node is named after its bulk yield, the heaviest-weighted drop worth keeping, rather than
  whatever sits first in its drop table. Drop-table order is arbitrary.
- Coal joins the ignored drops, so a rock that yields nothing but coal is not worth a pin.
- `Mine.ExcludedPrefabs` skips node prefabs that are scattered rather than worth returning to.
  A trailing `*` matches any ending; the six Deep North ice shards are excluded by default, while
  the larger ice formations still pin.
- `trove catalog` now prints each node's full drop table with weights.
- `trove` console command: `list`, `catalog`, `save`, `unforget`, `clear`.
