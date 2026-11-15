Friends pin
- An Admin pin publish updates meta/norcom-mod-pin.json, meta/server-pin.json, meta/server-compat.json, and meta/heal-hint.json together.
- While the launcher is open it polls that published pin, and Home Fix uses the same fetch: commit-ref raw, jsDelivr, GitHub Contents API, then cache-busted raw on main.
- Bare raw.githubusercontent.com/.../main/meta is not the source of truth. The baked LivePin string only warns. Jar checks use the jars in the pin that was fetched.

NORCOM 1.0.45
- Restores Xaero's Minimap 26.6.2 and Xaero's World Map 1.47.2 (client) after 1.0.44 removed them. Official pages: https://modrinth.com/mod/xaeros-minimap and https://modrinth.com/mod/xaeros-world-map
- Client profiles ship surface-only: minimap cave mode off (`minimap_cave_mode_allowed = false`) and world map cave mode off (`cave_mode_allowed = false`). The manual cave-mode keybind is not restored.
- The live server also needs both jars plus server profiles (`minimap_cave_mode_allowed = false` / `cave_mode_allowed = false` with `default_enforced_profile = default`) to enforce surface-only maps. Do not use Xaero fair-play codes.

NORCOM 1.0.35
- Graphics: Low / Medium / Medium High / High / Ultra (default High) + Fix Visuals one-click repair
- Patrix 32x basic + addon + models + Continuity CTM (Faithful/FA removed from pack)
- Complementary Unbound r5.9.3 (labPBR) storm-safe: Particle Rain 4.0.3 sole rain, vanilla rain off, Unbound waving rain/puddles off
- Removed Super Resolution and Wakes (Iris/shader conflicts)
- EMF/ETF/NEA kept for Patrix entity models
