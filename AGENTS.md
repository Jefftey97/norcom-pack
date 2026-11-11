# NORCOM pack

Public friends Minecraft pack (Fabric, packwiz) and the NORCOM Launcher that updates it. Friends already have a loader installed. This repo keeps that copy current.

## Layout

- `pack.toml`, `index.toml`, `mods/`, `config/`, `resourcepacks/`, `shaderpacks/` — the Fabric pack.
- `meta/` — pin files the open launcher polls (`norcom-mod-pin.json`, `server-pin.json`, `server-compat.json`, `heal-hint.json`).
- `launcher/manifest.json` — the feed older loaders already fetch. It points at the 2.0.45 bridge.
- `launcher/channel-v1.json` — the signed friends channel used after the bridge. Do not edit its payload or signature in this repo.
- `launcher/NorcomWpf-*.zip` — published packages, including rollback copies.
- `src/Norcom.Launcher.Core` — update, pin, and SHA-256 logic. Targets net10.0 and keeps net8.0 as an extra target. Checked-in launcher projects are version 3.0.6. AssemblyVersion stays 2.0.1.0 on purpose for the 2.0.45 bridge. Newer builds are the zips under `launcher/`.
- `src/Norcom.Launcher`, `src/Norcom.Installer`, `src/Norcom.Admin` — Windows WPF apps.
- `src/Norcom.Launcher.Tests` and `src/Norcom.Admin/Norcom.Admin.Tests` — xUnit.
- `tools/` — Windows smoke-swap scripts and marble-pack helpers.
- `rules.md` — player-facing server rules.

## Tests

The .NET SDK is not preinstalled on the Cursor Linux VM. `global.json` pins SDK 10.0.403 (latest patch roll-forward), and `Norcom.Launcher.Tests` targets net10.0, so install the .NET 10 SDK. Admin targets net8.0, so the .NET 8 runtime is also needed for its tests. With both installed:

```bash
dotnet test src/Norcom.Launcher.Tests/Norcom.Launcher.Tests.csproj
dotnet test src/Norcom.Admin/Norcom.Admin.Tests/Norcom.Admin.Tests.csproj
```

Those tests cover the checked-in source (Launcher on net10.0, Admin on net8.0). Windows and WPF behavior is verified on the owner's PC. See `.cursor/CLOUD.md`.

## Rules

- Friends keep the loader they already have. Every update goes through the existing signed feed. No reinstall and no new download path.
- Older loaders (for example 2.0.44) route through the 2.0.45 bridge before the newest version. Never skip the bridge.
- Never delete rollback copies until the new version is confirmed working.
- The feed must be signed and is time-limited (max 30 days). Never print, commit, move, or request the signing key. Signing happens only on the owner's PC.
- Never weaken signature or SHA-256 verification.
- This repo is public: no secrets, tokens, IPs, RCON details, or personal data.
- Cursor cloud results are Linux-only. Windows/WPF behavior is verified on the owner's PC.
- If a credential is missing, stop and ask. No workarounds.
- Small PRs, one goal each.
