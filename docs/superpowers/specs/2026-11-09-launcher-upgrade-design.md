# NORCOM Launcher upgrade design

Owner request: finish the NORCOM Launcher upgrade with Astra. Codex implements;
Astra independently reviews. Standing owner delegation covers routine design
choices. Minecraft remains off. The separate workstation candidate remains
awaiting its own Grok review and is outside this checkout.

## Current evidence

Complete source obtained from Jefftey97/norcom-pack at
`a773955d82ca214f808bc726450f0ff7b9f27565`, matching the handoff. The public legacy
feed still advertises 2.0.44. Existing WPF resource bundle is present and the
baseline compiles with 179 warnings and zero errors. Audited baseline: 107 pass,
9 fail, 3 unsafe-for-this-session cases excluded. All nine failures arise while
the friend launcher reads a stale local owner pin instead of the supplied feed.
These are baseline failures, not passing or skipped tests.

Verified private pre-edit backup: 55 launcher/state/release files, 794268074 bytes;
manifest SHA256 565745cc6fb441b6bb62f24db4d5b91f27378ce1312759da31a8028477f4d2e4.
Authentication contents remain private. No game or server action was performed.

## Approach selected by implementer

Retain the existing native/.NET stack for the compatibility bridge, then use
editable WPF for the successor UI. A browser renderer adds a runtime dependency
and does not solve recovery; a direct 3.x tip strands dormant legacy clients.
Permanent legacy-to-bridge routing plus a separately authenticated successor
channel preserves the old entry point and permits independently recoverable
successor payloads. The original legacy first hop retains its old limitations.

First repair concrete baseline defects: friend/client pin source isolation,
non-destructive authentication reads, and preservation of recovery backups.
Do not change game versions, published pins, Microsoft identity/client IDs,
saves, server state or workstation connections as part of launcher activation.

Signed release envelopes use exact payload bytes and an algorithm supplied by
the .NET cryptography library, with product/channel/version/hash/size/sequence
and validity bounds. Test keys cannot become production trust by accident.
Signing custody and public trust material must be concrete and verified before
release. Rollback does not lower accepted feed sequence.

Updater staging/transaction/health handling is independent from the renderer.
Pending and last-known-good bytes survive cleanup. Receipts bind attempt and
exact payload; paths stay under one installation root and reject reparse/link
escapes. Recovery verifies actual restored bytes before reporting success.
Auth and saves remain in existing locations; activation does not restore old
auth and thereby undo an explicit sign-out. A migration requires separate proof.

## Acceptance and limits

Compile and unit tests are separate from actual Windows process/GUI recovery
tests. Use isolated fake payloads and profiles for crash/timeout/reboot-journal
and tamper cases. Never start Minecraft to prove updater health. Public feed
publication requires the complete reviewed package and rollout evidence; local
changes alone do not finish the upgrade. Signing, real power-loss behavior and
live game acceptance cannot be claimed from fixture results.

The detailed independent architecture review is preserved in the parent
workspace's outputs/NORCOM-Launcher-Astra-foundation-design.md.
