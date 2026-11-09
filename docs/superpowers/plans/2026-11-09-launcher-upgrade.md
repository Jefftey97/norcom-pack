# NORCOM Launcher upgrade implementation plan

> For agentic workers: use superpowers:executing-plans for root implementation.
> Astra reviews interfaces and the final branch; root owns product edits.

**Goal:** Deliver the requested launcher upgrade through a recoverable compatible
bridge and a usable successor, preserving installed user state.

**Architecture:** Native bridge, authenticated successor channel, independent
recovery coordinator, editable WPF shell. Keep friend and owner authority separate.

**Tech stack:** Existing C#/.NET/WPF; baseline SDK 8.0.427 installed in workspace.

**Spec:** ../specs/2026-11-09-launcher-upgrade-design.md

## Global constraints

- Minecraft off; no Java/server/RCON lifecycle action, rebuild or game launch.
- Backup precedes source edits; preserve old binaries, auth and saves.
- No public tip until exact package review and recovery tests pass.
- Do not alter the separate workstation candidate or its review receipts.
- Preserve original test failures and explicitly account for unexecuted cases.

## Review focus

- Dormant clients must reach the bridge, including after a successor is released.
- New startup failure must preserve a usable prior payload and its state.
- Stale/tampered health markers and wrong-process identity cannot commit an update.
- Failed auth decryption must not erase the sole original session bytes.
- A machine with old owner server files must still use the intended friend feed.

## Task 1 — baseline and recovery preservation

Files: Core/Services/{PinGate,FileAuthStore,LauncherCleanup}.cs; Tests new
LauncherFoundationTests.cs and existing PlayUpdateTests.cs.
- [ ] Preserve meaningful failing baseline evidence and add auth/cleanup RED tests.
- [ ] Remove implicit owner-drive pin lookup; retain explicit local fixture override.
- [ ] Preserve unreadable auth originals; preserve recovery directories during cleanup.
- [ ] Run focused/full isolated tests, build and obtain Astra review.

## Task 2 — authenticated release boundary

Files: new Core/Updates release types, verifier and bounded downloader; tests;
SelfUpdateService routing and package gate call sites.
- [ ] Pin product/channel/signing/sequence and URI/size validation in negative tests.
- [ ] Implement verified successor envelope and route legacy/bridge separately.
- [ ] Integrate public trust packaging; no test-key or unsigned fallback in release.

## Task 3 — independent recovery

Files: new native updater/coordinator and Core/Updates journal/activation; App entry
dispatch; cleanup ownership; tests and disposable process harness.
- [ ] Write failure-window, path-boundary, exact health, restore and quarantine tests.
- [ ] Implement stage/verify/launch/health/commit/recovery, preserving known-good bytes.
- [ ] Exercise failures with fake native payloads; verify state and saves unchanged.

## Task 4 — successor usability and integration

Files: editable successor WPF app, view models, theme/assets and package targets.
- [ ] Retain sign-in, Play/Repair/settings behavior with clear status/recovery UI.
- [ ] Use the authenticated update/recovery path and explicit offline/unknown states.
- [ ] Verify native keyboard/scaling/launch behavior using a disposable profile.

## Task 5 — exact review, installation and handoff

- [ ] Freeze candidate, record complete tests and package hashes; Astra and Grok review.
- [ ] Verify recoverable local installation and EXE flows without starting Minecraft.
- [ ] Publish only reviewed release/channel bytes after concrete rollout gates.
- [ ] Report what shipped and the remaining acceptance limits honestly.

These boxes are a plan, not evidence of completion. Execution results go in the
local progress record and exact test reports.
