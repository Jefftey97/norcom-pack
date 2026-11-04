# NORCOM pack notes (1.0.7)

## New this version
- Default shader is now Complementary Unbound r5.9.3 (labPBR for Patrix). Complementary Reimagined stays in the pack as a second option.
- New installs get Unbound; existing installs keep their current shader until they pick Unbound in Video Settings → Shader Packs (iris.properties is preserve=true).

# NORCOM pack notes (1.0.5)

## New this version
- Removed AntiXray completely (Simple Eco OreShield remains the only ore-hiding).
- Added Crash Assistant 1.11.14 (client) for post-crash log analysis.
- Added MixinTrace Unofficial-TT-Port 2.0.0+26.1 (client+server) so crash stacks list mixins.
- First Aid remains fully removed (death limbo fix).

# NORCOM pack notes (1.0.4)

## New this version
- Removed First Aid completely (death limbo / knockout timeout bug).

# NORCOM pack notes (1.0.3)

## New this version
- Added damage tilt: camera jolts when you take a hit (client-only Modrinth build `damagetilt-1.1.0-26.3.jar`).

# NORCOM pack notes (1.0.2)

## New this version
- Mc2Discord (server; unconfigured - see admin/DISCORD-SETUP.txt)
- Styled Chat + Styled Player List + Placeholder API
- MiniMOTD (NORCOM dark/green/amber theme)
- simple eco - single NORCOM Coins currency (homes/TPA/RTP/claims modules disabled)
- Enhanced Celestials 2 (core + default lunar events + client shader support)
- Theseus Quests
- Easy NPC (core + config UI + Easy Model Entities)
- AudioPlayer + Simple Voice Chat Radio + Voice Chat Interaction (UDP 24454 tunnel pending)
- LambDynamicLights + Drop's Flashlight (client)
- Data Anchor (dependency)

## Economy
One virtual currency: NORCOM Coins via simple eco (/balance, /pay, shops, auctions).
Quest rewards and Easy NPC traders use /eco give|take. See admin/ECONOMY.txt after server boot.

## Skipped / pending
- Physics Mod Pro: never shipped; launcher helper opens Patreon then imports from Downloads
- Discord bot token: not invented; install idle until configured
- Voice radio UDP 24454 playit tunnel: not set up yet