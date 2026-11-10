# Cursor cloud (Linux VM)

This VM is Linux. A result here does not prove the Windows loader, the WPF window, or a Play update.

## Can run here

- Read the pack, `src/`, `launcher/manifest.json`, and `launcher/channel-v1.json`.
- Edit docs.
- Run the xUnit projects after installing the .NET SDK (it is not on the image). `global.json` pins SDK 10.0.403 (latest patch roll-forward). Launcher tests target net10.0, so install the .NET 10 SDK. Admin tests target net8.0, so the .NET 8 runtime is also needed:

```bash
dotnet test src/Norcom.Launcher.Tests/Norcom.Launcher.Tests.csproj
dotnet test src/Norcom.Admin/Norcom.Admin.Tests/Norcom.Admin.Tests.csproj
```

Those tests exercise the checked-in source (launcher projects are version 3.0.3; Launcher on net10.0, Admin on net8.0). They do not execute the published `launcher/NorcomWpf-*.zip` packages.

## Cannot run here

- NORCOM Launcher (`net10.0-windows`), NORCOM Admin, and Install-NORCOM (`net8.0-windows`, WPF). Play, the GUI, and self-update are verified on the owner's PC.
- `tools/hot-swap-launcher.ps1` and `tools/stamp-friends-payload.ps1`.
- Signing `launcher/channel-v1.json`, or any step that needs the signing key. The key stays on the owner's PC. Do not print, commit, move, or request it, and do not invent a replacement key.
- Publishing the feed, repointing `launcher/manifest.json`, or deleting `launcher/NorcomWpf-*.zip` rollback copies. Rollback copies stay until the new version is confirmed working on the owner's PC.
- A task that needs a missing credential. Stop and ask. Do not work around it.

Installing the public .NET 10 SDK and the .NET 8 runtime so the tests can run is fine. That is not a substitute for a missing secret.
